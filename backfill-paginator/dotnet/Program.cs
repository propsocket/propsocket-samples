// backfill-paginator (.NET) — full backfill of all four PropSocket entities.
//
// Pages through properties, units, residents, leases (parents before children),
// upserting each record into a local JSON key/value store keyed by id. Because we
// key on id, re-running is idempotent (a no-op for already-seen records).
//
// This is the sample that must honor rate limits: on HTTP 429 it sleeps exactly
// the server-provided Retry-After seconds, then retries. We do NOT invent a
// backoff — we honor the server's wait.
//
// BCL only: System.Net.Http.HttpClient + System.Text.Json. See README.md and the
// shared API contract.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// Order matters: parents (properties, units) before children (residents, leases).
string[] entities = { "properties", "units", "residents", "leases" };

// Backfill uses the max page size to minimize round-trips.
const int PageLimit = 100;

// Stable pagination requires a deterministic sort. Ascending created_at keeps the
// offset window stable while new records land at the end.
const string OrderBy = "created_at:asc";

const string DefaultBaseUrl = "https://api.propsocket.io/v1";

// Local upsert sink. NOT a real DB — a reference key/value store keyed by id.
// In production this would be your warehouse/database upsert.
const string StorePath = "backfill-store.json";

var apiKey = Environment.GetEnvironmentVariable("PROPSOCKET_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("error: PROPSOCKET_API_KEY is not set (copy .env.example to .env)");
    return 1;
}

var baseUrl = (Environment.GetEnvironmentVariable("PROPSOCKET_BASE_URL") ?? DefaultBaseUrl)
    .TrimEnd('/');

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

// Load any existing store so re-runs are idempotent and incremental. Keys are
// record ids; values are the raw record JSON (kept as JsonElement, opaque to us).
var store = LoadStore(StorePath);

try
{
    foreach (var entity in entities)
    {
        var (seen, inserted) = await BackfillEntity(http, baseUrl, entity, store);
        // Persist after each entity so a crash mid-run loses at most one entity's tail.
        SaveStore(StorePath, store);
        Console.WriteLine($"{entity}: {seen} fetched, {inserted} new (store now {store.Count} total)");
    }
}
catch (ApiException ex)
{
    Console.Error.WriteLine(ex.Message);
    SaveStore(StorePath, store); // don't lose progress already made
    return 1;
}

Console.WriteLine();
Console.WriteLine($"Backfill complete. {store.Count} records in {StorePath}.");
return 0;

// Pages through one entity, upserting every record. Returns (recordsSeen, newlyInserted).
async Task<(int seen, int inserted)> BackfillEntity(
    HttpClient client, string root, string entity, Dictionary<string, JsonElement> sink)
{
    var seen = 0;
    var inserted = 0;
    var offset = 0;

    while (true)
    {
        var url = $"{root}/{entity}?limit={PageLimit}&offset={offset}" +
                  $"&order-by={Uri.EscapeDataString(OrderBy)}";
        var page = await GetPage(client, url);

        foreach (var rec in page.Results)
        {
            // The id is the upsert key. The spec's example records always carry one.
            if (!rec.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            var id = idEl.GetString()!;
            seen++;

            // Upsert: insert-or-overwrite keyed on id. Counting only first-sight ids
            // makes the "new" tally meaningful across idempotent re-runs.
            if (!sink.ContainsKey(id))
                inserted++;
            sink[id] = rec.Clone(); // Clone detaches from the parsed document we dispose.
        }

        if (!page.Meta.HasMore)
            break;
        offset += PageLimit;
    }

    return (seen, inserted);
}

// Fetches one page, transparently handling 429 by sleeping the server's Retry-After.
async Task<ListPage> GetPage(HttpClient client, string url)
{
    while (true)
    {
        using var resp = await client.GetAsync(url);

        if (resp.StatusCode == (HttpStatusCode)429)
        {
            // Honor the server's wait exactly. Retry-After is in seconds; default to a
            // small floor if the header is missing or unparseable.
            var wait = resp.Headers.RetryAfter?.Delta
                       ?? (TryParseSeconds(resp.Headers, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(1));
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            Console.Error.WriteLine($"  rate limited (429); sleeping {wait.TotalSeconds:0}s per Retry-After...");
            await Task.Delay(wait);
            continue; // retry the same page/offset
        }

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw ApiException.FromResponse(resp, body);

        var page = JsonSerializer.Deserialize<ListPage>(body, JsonOpts.Default)
                   ?? throw new ApiException("error: empty/unparseable response body");
        return page;
    }
}

static bool TryParseSeconds(HttpResponseHeaders headers, out int seconds)
{
    seconds = 0;
    if (headers.TryGetValues("Retry-After", out var vals))
        foreach (var v in vals)
            if (int.TryParse(v, out seconds))
                return true;
    return false;
}

static Dictionary<string, JsonElement> LoadStore(string path)
{
    if (!File.Exists(path))
        return new Dictionary<string, JsonElement>();
    try
    {
        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        return loaded ?? new Dictionary<string, JsonElement>();
    }
    catch (JsonException)
    {
        // Corrupt store: start fresh rather than abort the backfill.
        return new Dictionary<string, JsonElement>();
    }
}

static void SaveStore(string path, Dictionary<string, JsonElement> store)
{
    var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// The offset-pagination envelope. Results stay as raw JsonElement so we upsert the
// full record verbatim without modeling every field.
sealed class ListPage
{
    [JsonPropertyName("meta")] public Meta Meta { get; set; } = new();
    [JsonPropertyName("results")] public List<JsonElement> Results { get; set; } = new();
}

sealed class Meta
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; set; }
}

// RFC 7807 problem+json error, surfaced with request_id for support.
sealed class Problem
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("request_id")] public string? RequestId { get; set; }
}

sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }

    public static ApiException FromResponse(HttpResponseMessage resp, string body)
    {
        Problem? p = null;
        try { p = JsonSerializer.Deserialize<Problem>(body, JsonOpts.Default); }
        catch (JsonException) { /* body may not be problem+json */ }

        var requestId = !string.IsNullOrEmpty(p?.RequestId)
            ? p!.RequestId
            : (resp.Headers.TryGetValues("x-request-id", out var vals)
                ? string.Join(",", vals)
                : "(none)");
        var detail = p?.Detail ?? p?.Title ?? resp.ReasonPhrase ?? "(no detail)";
        var title = p?.Title ?? "Request failed";

        return new ApiException(
            $"Error {(int)resp.StatusCode} {title}: {detail} [request_id={requestId}]");
    }
}
