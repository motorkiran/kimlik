using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

// Measures how many requests per second a running Kimlik answers, and how fast, for the requests applications
// make most: tokens for service clients, discovery and keys, and Management API reads.
//
//   dotnet run -c Release --project tools/Kimlik.LoadTest -- <kimlik url> <client id> <client secret> [seconds] [concurrency]
//
// The client must be a service client allowed the kimlik scope and holding kimlik.users:read. Raise
// Kimlik:RateLimits:ProtocolRequestsPerMinute on the server first, or the token endpoint answers 429.
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: <kimlik url> <client id> <client secret> [seconds] [concurrency]");
    return 1;
}

var baseAddress = new Uri(args[0]);
var (clientId, clientSecret) = (args[1], args[2]);
var duration = TimeSpan.FromSeconds(args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 20);
var concurrency = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 32;

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = concurrency, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = baseAddress,
};

var token = await RequestTokenAsync();

Console.WriteLine($"{"Scenario",-34} {"Requests/s",10} {"p50 ms",8} {"p95 ms",8} {"p99 ms",8} {"Errors",7}");
await RunAsync("Discovery document", () => http.GetAsync(".well-known/openid-configuration"));
await RunAsync("Signing keys (JWKS)", () => http.GetAsync(".well-known/jwks"));
await RunAsync("Client credentials token", () => http.PostAsync("connect/token", TokenRequest()));
await RunAsync("Management API: list users", () =>
{
    var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/users?limit=50");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return http.SendAsync(request);
});
return 0;

FormUrlEncodedContent TokenRequest() => new(
[
    new("grant_type", "client_credentials"),
    new("client_id", clientId),
    new("client_secret", clientSecret),
    new("scope", "kimlik"),
]);

async Task<string> RequestTokenAsync()
{
    using var response = await http.PostAsync("connect/token", TokenRequest());
    response.EnsureSuccessStatusCode();
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return body.RootElement.GetProperty("access_token").GetString()!;
}

async Task RunAsync(string scenario, Func<Task<HttpResponseMessage>> send)
{
    // A short warm-up, so that JIT compilation and connection setup do not count.
    var warmUpEnds = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
    while (Stopwatch.GetTimestamp() < warmUpEnds)
    {
        using var _ = await send();
    }

    var latencies = new List<double>[concurrency];
    var errors = 0;
    var ends = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
    var started = Stopwatch.GetTimestamp();

    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
    {
        var own = latencies[worker] = [];
        while (Stopwatch.GetTimestamp() < ends)
        {
            var sent = Stopwatch.GetTimestamp();
            try
            {
                using var response = await send();
                await response.Content.LoadIntoBufferAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref errors);
                }
            }
            catch (HttpRequestException)
            {
                Interlocked.Increment(ref errors);
            }

            own.Add(Stopwatch.GetElapsedTime(sent).TotalMilliseconds);
        }
    }));

    var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
    var all = latencies.SelectMany(own => own).Order().ToArray();
    double Percentile(double p) => all[Math.Min(all.Length - 1, (int)(p * all.Length))];

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{scenario,-34} {all.Length / elapsed,10:F0} {Percentile(0.50),8:F1} {Percentile(0.95),8:F1} {Percentile(0.99),8:F1} {errors,7}"));
}
