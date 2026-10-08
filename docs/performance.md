# Performance

`tools/Kimlik.LoadTest` measures the requests applications make most, against a running Kimlik. These are the numbers of one run, to show the order of magnitude; measure on your own hardware before sizing an installation.

## Results

One Kimlik instance (Release build, `Production` environment, logging at `Warning`) and PostgreSQL 18 on the same Apple M3 Max laptop (14 cores), 32 concurrent connections, 15 seconds per scenario after a warm-up:

| Scenario | Requests/s | p50 ms | p95 ms | p99 ms |
|---|---:|---:|---:|---:|
| Discovery document | 54,995 | 0.5 | 0.9 | 1.6 |
| Signing keys (JWKS) | 56,306 | 0.5 | 0.7 | 1.6 |
| Client credentials token | 1,157 | 26.3 | 40.8 | 54.0 |
| Management API: list users | 4,705 | 6.5 | 10.2 | 12.9 |

What the numbers say:

- **Discovery and keys** cost next to nothing, and resource servers cache them anyway.
- **Tokens** are the expensive request, by design: Kimlik checks the client secret with a deliberately slow hash (PBKDF2), signs the token and records it, so that it can be revoked. Service clients should reuse a token until it nears expiry, as `Kimlik.Client` does, rather than request one per call.
- **Management API** requests validate the access token against its database entry, so a revoked token stops working at once, and stay in single-digit milliseconds.

## Running it

Start Kimlik with the load test's service client and without the token endpoint's rate limit, which would otherwise answer most requests with 429:

```bash
export Kimlik__Provisioning__FilePath=$PWD/tools/Kimlik.LoadTest/provisioning.json
export LoadTest__ClientSecret=a-long-random-secret
export Kimlik__RateLimits__ProtocolRequestsPerMinute=1000000
export Logging__LogLevel__Default=Warning
dotnet run -c Release --project src/Kimlik.Server
```

Then, in another terminal:

```bash
dotnet run -c Release --project tools/Kimlik.LoadTest -- http://localhost:5080/ load-test a-long-random-secret 15 32
```

The arguments are Kimlik's address, the client's ID and secret, the seconds per scenario and the number of concurrent connections.
