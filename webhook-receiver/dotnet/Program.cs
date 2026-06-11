// webhook-receiver (.NET) — verify PropSocket webhook signatures and process events.
//
// PropSocket POSTs JSON with an `X-PropSocket-Signature` header: a lowercase hex
// HMAC-SHA256 over the RAW, unmodified request body, using PROPSOCKET_WEBHOOK_SECRET.
//
// Three things this sample gets deliberately right:
//
//  1. RAW BODY FIRST. We read the exact bytes off the request stream BEFORE any JSON
//     parsing and verify the signature against those bytes. Re-serializing parsed JSON
//     would change whitespace/key order and break the HMAC.
//
//  2. CONSTANT-TIME COMPARE. Signatures are compared with
//     CryptographicOperations.FixedTimeEquals to avoid leaking equality timing.
//
//  3. ACK FAST, PROCESS ASYNC. We verify + dedupe synchronously, return 2xx within
//     well under the 10s budget, then hand the event to a background worker via a
//     Channel. Delivery is at-least-once, so the worker is idempotent: it dedupes on
//     the event `id` (prefixed `evt_`). The in-memory dedupe set is fine for a sample;
//     in production it must be durable (e.g. Redis/DB) with a TTL outlasting the
//     ~7h36m retry window (immediate, 30s, 1m, 5m, 30m, 1h, 6h → dead-letter).

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

var secret = Environment.GetEnvironmentVariable("PROPSOCKET_WEBHOOK_SECRET");
if (string.IsNullOrWhiteSpace(secret))
{
    Console.Error.WriteLine("error: PROPSOCKET_WEBHOOK_SECRET is not set (copy .env.example to .env)");
    return 1;
}
var secretBytes = Encoding.UTF8.GetBytes(secret);

var port = Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } p && int.TryParse(p, out var parsed)
    ? parsed
    : 4000;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Background processing queue. Unbounded is fine for a sample; bound it in prod.
var queue = Channel.CreateUnbounded<WebhookEvent>();

// Dedupe set keyed on event id. In-memory => resets on restart. Prod: durable + TTL.
var processed = new HashSet<string>();
var processedLock = new object();

var app = builder.Build();

// Single background worker drains the queue and "processes" events idempotently.
var worker = Task.Run(async () =>
{
    await foreach (var evt in queue.Reader.ReadAllAsync())
    {
        bool firstTime;
        lock (processedLock)
        {
            firstTime = processed.Add(evt.Id);
        }

        if (!firstTime)
        {
            // Shouldn't usually reach here (we dedupe before enqueue too), but the
            // worker stays idempotent as a defense in depth.
            app.Logger.LogInformation("event {Type} {Id} — duplicate, skipped in worker", evt.Type, evt.Id);
            continue;
        }

        // Real processing would go here. The sample just logs type + id.
        app.Logger.LogInformation("event {Type} {Id} — processed", evt.Type, evt.Id);
    }
});

app.MapGet("/", () => Results.Text("PropSocket webhook receiver — POST events to /webhooks\n"));

app.MapPost("/webhooks", async (HttpRequest req) =>
{
    // 1. Read the RAW bytes before any parsing. We read straight from the body stream
    //    into a buffer so the HMAC is computed over exactly what PropSocket signed.
    byte[] rawBody;
    using (var ms = new MemoryStream())
    {
        await req.Body.CopyToAsync(ms);
        rawBody = ms.ToArray();
    }

    // 2. Verify the signature in constant time.
    var provided = req.Headers["X-PropSocket-Signature"].ToString();
    if (string.IsNullOrEmpty(provided) || !SignatureValid(rawBody, provided, secretBytes))
    {
        app.Logger.LogWarning("rejected: bad or missing signature");
        return Results.Unauthorized(); // 401
    }

    // 3. Parse only after the signature checks out. Unparseable body => 400.
    WebhookEvent? evt;
    try
    {
        evt = JsonSerializer.Deserialize<WebhookEvent>(rawBody, JsonOpts.Default);
    }
    catch (JsonException)
    {
        evt = null;
    }
    if (evt is null || string.IsNullOrEmpty(evt.Id))
    {
        app.Logger.LogWarning("rejected: unparseable body or missing event id");
        return Results.BadRequest(new { error = "unparseable webhook body" }); // 400
    }

    // 4. Idempotency: dedupe on the event id. Duplicates still ACK 2xx (already
    //    handled) but are NOT re-enqueued.
    bool alreadySeen;
    lock (processedLock)
    {
        alreadySeen = processed.Contains(evt.Id);
    }
    if (alreadySeen)
    {
        app.Logger.LogInformation("event {Type} {Id} — duplicate, ack only", evt.Type, evt.Id);
        return Results.Ok(new { status = "duplicate" }); // 2xx
    }

    // 5. ACK fast, process async. Enqueue and return 2xx immediately.
    await queue.Writer.WriteAsync(evt);
    return Results.Ok(new { status = "accepted" }); // 2xx, well within the 10s budget
});

app.Logger.LogInformation("webhook receiver listening on http://0.0.0.0:{Port}/webhooks", port);
app.Run();
return 0;

// Constant-time verification of the lowercase-hex HMAC-SHA256 over the raw body.
static bool SignatureValid(byte[] rawBody, string providedHex, byte[] key)
{
    var expected = HMACSHA256.HashData(key, rawBody); // 32 bytes

    // Decode the provided hex into bytes. A bad/odd-length hex string is invalid.
    if (!TryFromHex(providedHex, out var providedBytes))
        return false;

    // FixedTimeEquals returns false (in constant time) for length mismatch too.
    return CryptographicOperations.FixedTimeEquals(expected, providedBytes);
}

static bool TryFromHex(string hex, out byte[] bytes)
{
    bytes = Array.Empty<byte>();
    hex = hex.Trim();
    if (hex.Length == 0 || hex.Length % 2 != 0)
        return false;
    try
    {
        bytes = Convert.FromHexString(hex); // accepts upper/lower hex
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
}

static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// Minimal event shape. The receiver only needs id + type; the rest is logged context.
sealed class WebhookEvent
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("organization_id")] public string? OrganizationId { get; set; }
    [JsonPropertyName("integration_id")] public string? IntegrationId { get; set; }
    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}
