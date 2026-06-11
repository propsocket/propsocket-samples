// rest-consumer (.NET) — list one PropSocket entity, page through results, print rows.
//
// Reads PROPSOCKET_API_KEY (and optional PROPSOCKET_BASE_URL) from the environment.
// BCL only: System.Net.Http.HttpClient + System.Text.Json. See README.md and the
// shared API contract.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// Swap this single constant to consume a different entity. The API exposes four
// top-level list endpoints: properties, units, residents, leases.
const string Entity = "properties";

// rest-consumer stays simple: a modest page size is fine (the API max is 100).
const int PageLimit = 25;

const string DefaultBaseUrl = "https://api.propsocket.io/v1";

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

var total = 0;
var offset = 0;

while (true)
{
    var url = $"{baseUrl}/{Entity}?limit={PageLimit}&offset={offset}";
    using var resp = await http.GetAsync(url);
    var body = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
    {
        // The backfill sample handles 429/Retry-After; here any non-2xx is fatal.
        // Surface request_id so the user can quote it to support.
        ReportError(resp, body);
        return 1;
    }

    var page = JsonSerializer.Deserialize<ListResponse>(body, JsonOpts.Default);
    if (page is null)
    {
        Console.Error.WriteLine("error: empty/unparseable response body");
        return 1;
    }

    foreach (var r in page.Results)
    {
        // Contract: print id, name, status per row.
        Console.WriteLine($"{r.Id,-32}  {r.Name,-40}  {r.Status}");
        total++;
    }

    // Last page when the server says there's no more; otherwise advance the offset
    // by the page size — that's the offset-pagination contract.
    if (!page.Meta.HasMore)
        break;
    offset += PageLimit;
}

Console.WriteLine();
Console.WriteLine($"Total {Entity}: {total}");
return 0;

// Turns a non-2xx response into a readable error line. request_id mirrors the
// x-request-id response header; fall back to the header if the body lacks it.
static void ReportError(HttpResponseMessage resp, string body)
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

    Console.Error.WriteLine(
        $"Error {(int)resp.StatusCode} {title}: {detail} [request_id={requestId}]");
}

static class JsonOpts
{
    // Field names on the wire are camelCase for the envelope (meta/results) and
    // snake_case for record fields; explicit [JsonPropertyName] handles both, so a
    // permissive case-insensitive matcher is enough.
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// The offset-pagination envelope every list endpoint returns.
sealed class ListResponse
{
    [JsonPropertyName("meta")] public Meta Meta { get; set; } = new();
    [JsonPropertyName("results")] public List<Record> Results { get; set; } = new();
}

sealed class Meta
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; set; }
}

// Only the fields the spec asks us to print. The API returns more (x_id,
// integration_id, type, total_units, timestamps, deleted_at); we decode what we show.
sealed class Record
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

// RFC 7807 problem+json error body.
sealed class Problem
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("request_id")] public string? RequestId { get; set; }
}
