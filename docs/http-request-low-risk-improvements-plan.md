# HTTP Request Low-Risk Improvements Plan

## Purpose

Plan incremental HTTP request improvements before implementation. Scope is the runtime HTTP request path used by blocks and tests:

- `RuriLib/Blocks/Requests/Http/Methods.cs`
- `RuriLib/Functions/Http/`
- `RuriLib.Http/`
- `RuriLib.Tests/Functions/Http/`
- `RuriLib.Http.Tests/`

This plan does not redesign the block API, job runtime, database, web API, frontend, or vendored `Libraries/TlsClient.NET`.

## Implementation Status

Implemented in this pass:

- `RuriLibHttp` receive timeout propagation, bounded pooled-client accounting, safe connection-return checks, and tested keep-alive for delimited HTTP/1.1 responses.
- HTTP log redaction/truncation and disabled-logger payload preview avoidance.
- `TlsClientRequestHandler` cookie-parsing parity and multipart missing-file handling.
- `SystemNet` shared `HttpClient` reuse with leased cache entries and idle cleanup.
- `ProxyClientHandler` response parsing/stream reading fix for proxy-check and compatibility paths.
- Deterministic local transport tests replacing `httpbin.org` and `nghttp2.org` dependencies.

Not changed:

- Public block options and LoliCode/C# emission contracts.
- Vendored `Libraries/TlsClient.NET` internals.
- Database, frontend, web API, job scheduling, or deployment infrastructure.

## Assumptions And Unknowns

- `HttpLibrary.RuriLibHttp` should remain the custom `RuriLib.Http.RLHttpClient` transport.
- `HttpRequestHandler` remains the shared pipeline owner for normalization, redirect handling, timeout token creation, logging, and response mapping.
- Transport-specific handlers remain responsible only for sending `NormalizedHttpRequest` and returning `NormalizedHttpResponse`.
- `TlsClient` native library availability varies by developer/CI machine; tests must skip deterministically when unavailable.
- `RuriLibHttp` keep-alive is enabled only for fully consumed, safely delimited responses; explicit close semantics are covered by regression tests.
- `ProxyClientHandler`/`HttpFactory.GetProxiedHandler` is in scope only for correctness of existing proxy-check and compatibility behavior.
- Unknown: whether any external consumers reference public lower-level HTTP types in `RuriLib.Http`. Do not remove or obsolete public types in this pass without a separate compatibility decision.

## System Architecture

Current HTTP request flow:

1. Block entry points in `RuriLib/Blocks/Requests/Http/Methods.cs` choose a transport handler from `HttpRequestOptions.HttpLibrary`.
2. Concrete handlers in `RuriLib/Functions/Http/*RequestHandler.cs` create `NormalizedHttpRequest`.
3. `HttpRequestHandler.ExecutePipelineAsync` validates, logs, applies a linked timeout token, sends via the selected transport, maps response state, and follows redirects through `HttpRedirectPolicy`.
4. Transport adapters:
   - `HttpClientRequestHandler` uses `System.Net.Http.HttpClient`.
   - `RLHttpClientRequestHandler` uses `RuriLib.Http.RLHttpClient`.
   - `TlsClientRequestHandler` uses `TlsClient.Native`.
5. `HttpResponseMapper` writes response code, address, headers, cookies, `RAWSOURCE`, and `SOURCE` back to `BotData`.

Module boundaries to preserve:

- Shared semantics belong in `RuriLib/Functions/Http/`.
- Raw socket transport internals belong in `RuriLib.Http/`.
- Vendored TLS implementation is not modified unless adapter-level fixes cannot solve the issue.
- Regression tests belong in `RuriLib.Tests` for block/runtime behavior and `RuriLib.Http.Tests` for transport internals.

## Contracts And Data Flow

Contracts to preserve:

- `HttpRequestOptions` is the block/runtime input contract. Do not rename or remove existing options.
- `NormalizedHttpRequest` is the internal transport-neutral request model. Shared request semantics should be represented here before reaching transport adapters.
- `NormalizedHttpResponse.Headers` must preserve duplicate header values through `Dictionary<string, List<string>>`, especially `Set-Cookie`, redirects, and duplicate-casing headers.
- `NormalizedHttpResponse.RawBody` must remain byte-preserving. Do not text-round-trip binary or non-UTF8 responses in transport adapters.
- `BotData.COOKIES` is the durable runtime cookie store. Transport adapters should not introduce additional persistent cookie state unless explicitly tested and documented.
- `HttpRequestHandler.ExecutePipelineAsync` owns timeout token creation, redirect loop control, response mapping, and exception logging.

Existing contract risks to verify:

- `RuriLib.Http.Models.HttpResponse.Headers` is currently single-value per header. `RLHttpClientRequestHandler` must not lose important duplicate values when mapping this into `NormalizedHttpResponse`.
- `RuriLib.Http.HttpResponseBuilder` currently mutates `response.Request.Cookies` while parsing `Set-Cookie`; this overlaps with `HttpResponseMapper`. Do not expand this side effect. Add tests before changing it.
- Lower-level response builders use `int` content lengths. Large response behavior is out of scope for this pass unless a targeted test exposes an immediate bug.

## Phase 0. Baseline And Test Harness

### Objective

Create a reliable validation and measurement baseline before touching HTTP behavior.

### Scope

- Existing HTTP parity tests.
- Existing `RuriLib.Http.Tests` tests that still call external services.
- Local HTTP test fixtures already present in `RuriLib.Tests/Infrastructure`.

### Technical Tasks

- Run targeted tests before implementation:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj --filter Http`
  - `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`
- Identify tests that require external network access, especially `httpbin.org` and `nghttp2.org`.
- Decide whether to extend existing `RuriLib.Tests/Infrastructure/TestHttpServer.cs` or add equivalent fixtures under `RuriLib.Http.Tests`.
- Add or prepare local scenarios for:
  - connection reuse vs `Connection: close`
  - slow response greater than 10 seconds
  - high-concurrency pooled client acquisition/release
  - logging disabled with large raw/multipart payload
  - `TlsClient` disabled cookie parsing, when native library is available
- Capture lightweight baseline metrics for later comparison:
  - connection count for repeated same-host requests
  - maximum active/queued pooled clients under concurrent requests
  - log entry size for large payload requests
  - elapsed time for local repeated-request smoke tests, used directionally only

### Dependencies

- Current test fixtures compile and can emit custom headers/bodies.
- CI/dev environment has .NET 8 SDK.

### Risks/Blockers

- `TlsClient` tests may not run locally if `tls-client.dll` is unavailable.
- External-network tests may already be flaky or blocked.

### Deliverables

- Baseline test result summary.
- List of local fixture gaps.
- New or updated test scaffolding only where required for later phases.

### Validation/Testing Criteria

- Existing HTTP tests either pass or failures are documented before code changes.
- New local fixtures can capture method, first request line, headers, raw body, connection count, and response timing.
- Baseline measurements are recorded without turning timing into brittle pass/fail assertions.

### Exit Criteria

- There is a deterministic test path for each planned behavior change.
- Any pre-existing failures are explicitly separated from new regressions.
- Performance claims have a concrete before/after measurement method, even if the initial gate is functional correctness.

## Phase 1. RuriLibHttp Safety Fixes

### Objective

Fix the highest-risk `RuriLibHttp` reliability issues without changing default wire behavior.

### Scope

- `RuriLib/Functions/Http/RLHttpClientRequestHandler.cs`
- `RuriLib.Http/RLHttpClient.cs`
- `RuriLib.Http/Models/HttpRequest.cs`
- `RuriLib.Http/HttpResponseBuilder.cs`
- Tests in `RuriLib.Tests/Functions/Http/` and `RuriLib.Http.Tests/`

### Technical Tasks

- Fix `RLHttpClientRequestHandler` pooled-client accounting:
  - Ensure clients created beyond `MaxClientsPerKey` are not returned to the shared queue unless they are counted.
  - Prevent `ActiveClients` from going negative during cleanup.
  - Add a small testable helper if direct private reflection tests become brittle.
- Fix raw transport connection reuse safety:
  - Do not return connections to the pool when the request or response has `Connection: close`.
  - Detect response `Connection: close` and HTTP/1.0 close semantics before pooling.
  - Add a regression test that a closed connection is not reused.
- Align `HttpResponseBuilder.ReceiveTimeout` with configured timeouts:
  - Add a receive timeout property to `RuriLib.Http.RLHttpClient` or pass timeout into the builder.
  - Normalize `TimeSpan.Zero` and `Timeout.InfiniteTimeSpan` consistently with existing timeout helpers.
  - Avoid an internal 10-second timeout overriding a longer request timeout.
- Preserve the current default `Connection: Close` behavior in this phase. Do not enable keep-alive by default here.

### Dependencies

- Phase 0 local server must support slow responses and connection counting.
- Existing `HttpFactory.GetRLHttpClient` remains the construction path for `RuriLibHttp`.

### Risks/Blockers

- Pooling changes affect high-concurrency jobs and must be tested under parallel load.
- Direct tests of private pool internals can become brittle; prefer extracting narrow lifecycle helpers if needed.

### Deliverables

- Bounded pooled-client lifecycle.
- Safe connection-return rules for currently closed connections.
- Configurable or caller-aligned response receive timeout.
- Regression tests for pool cap, closed-connection handling, and slow response timeout behavior.

### Validation/Testing Criteria

- `RuriLibHttp` does not reuse a connection after `Connection: close`.
- Pool size remains bounded after repeated concurrent requests.
- A request configured above 10 seconds can receive a slow response without hidden timeout failure.
- Existing redirect, cookie, multipart, and binary parity tests still pass.

### Exit Criteria

- No known dead-socket reuse path remains in `RuriLibHttp`.
- Timeout behavior is controlled by explicit request/settings values, not a hidden constant.
- Default request wire behavior is unchanged except for not pooling known-closed connections.

## Phase 2. Gated RuriLibHttp Keep-Alive Enablement

### Objective

Enable real connection reuse for `RuriLibHttp` only if tests show it is safe.

### Scope

- `RuriLib.Http/Models/HttpRequest.cs`
- `RuriLib.Http/RLHttpClient.cs`
- `RuriLib.Http/HttpResponseBuilder.cs`
- Tests in `RuriLib.Http.Tests/`

### Technical Tasks

- Add tests for repeated same-host HTTP/1.1 requests with:
  - explicit `Connection: keep-alive`
  - no `Connection` header
  - explicit `Connection: close`
  - responses with `Content-Length`
  - chunked responses
  - response-body-until-close scenarios
- Remove the default `Connection: Close` header only after the tests above prove pool reuse is safe for delimited bodies.
- Keep non-delimited body-until-close responses out of the pool.
- Add connection-count assertions that distinguish "no dead reuse" from "actual keep-alive reuse".

### Dependencies

- Phase 1 safety fixes must be complete.
- Local test server must support multiple requests on a single accepted connection.

### Risks/Blockers

- Some servers rely on connection close to delimit bodies.
- Incorrect pooling can corrupt the next response if unread bytes remain on the stream.
- Proxy behavior may differ from direct connections.

### Deliverables

- Gated keep-alive behavior for safe HTTP/1.1 responses.
- Tests proving reuse only happens when the prior response was fully delimited and consumed.

### Validation/Testing Criteria

- Two sequential delimited responses can use one TCP connection.
- Responses using close-delimited bodies are not pooled.
- Explicit `Connection: close` is respected.
- Existing parity tests pass.

### Exit Criteria

- Keep-alive is either safely enabled with tests, or explicitly deferred with Phase 1 safety fixes still retained.

## Phase 3. Logging, Observability, And Sensitive Data Handling

### Objective

Reduce HTTP logging overhead and avoid leaking credentials while preserving useful debugging output.

### Scope

- `RuriLib/Functions/Http/HttpRequestNormalizer.cs`
- `RuriLib/Functions/Http/HttpPipelineLogger.cs`
- `RuriLib/Functions/Http/HttpResponseMapper.cs`
- `RuriLib/Logging/BotLogger.cs` only if existing logger-level truncation is insufficient.

### Technical Tasks

- Skip log-only payload generation when `data.Logger.Enabled` is false:
  - Raw request base64 preview.
  - Multipart serialized preview.
  - Full request log rendering.
- Add centralized HTTP log redaction for sensitive names:
  - `Authorization`
  - `Proxy-Authorization`
  - `Cookie`
  - `Set-Cookie`
  - likely token/password API-key header names by exact or suffix match.
- Cap request and response payload previews with a small constant.
- Include original byte/char counts in truncated log entries.
- Keep `BotData.RAWSOURCE`, `BotData.SOURCE`, headers, and cookies behavior unchanged.

### Dependencies

- Existing `IBotLogger.Enabled` behavior remains authoritative.
- No new configuration surface is required unless product owners require adjustable preview limits.

### Risks/Blockers

- Some users may rely on full request/response debug logs for troubleshooting.
- Redaction policy must avoid hiding non-sensitive operational headers unexpectedly.

### Deliverables

- Redacted HTTP request/response logs.
- Bounded log payload previews.
- Tests for disabled logger avoiding expensive preview generation where feasible.

### Validation/Testing Criteria

- HTTP behavior is identical with logging enabled or disabled.
- Sensitive header values do not appear in bot logs.
- Large payload logs are truncated but retain total size metadata.
- Large payload tests verify bounded log size, not exact formatting.

### Exit Criteria

- Logging can no longer materially amplify memory use for large HTTP payloads.
- Credential-bearing headers are not emitted in plaintext by HTTP pipeline logs.

## Phase 4. TlsClient Adapter Parity

### Objective

Make `TlsClientRequestHandler` behavior match shared HTTP semantics where the adapter currently owns divergent state or file handling.

### Scope

- `RuriLib/Functions/Http/TlsClientRequestHandler.cs`
- Tests in `RuriLib.Tests/Functions/Http/`

### Technical Tasks

- Cookie-state parity:
  - Disable native cookie jar when `request.DisableCookieParsing` is true.
  - Prefer shared `data.COOKIES` as the durable cookie state across transports.
  - Add test coverage for `DisableCookieParsing` with two sequential requests.
- Multipart file handling:
  - Throw on missing file instead of silently sending an empty file part.
  - Reuse `FileUtils.ThrowIfNotInCWD` behavior.
  - Avoid `File.ReadAllBytes` when a stream copy is enough before final base64 serialization.
- Keep changes in the adapter; do not edit vendored `Libraries/TlsClient.NET`.

### Dependencies

- Native `TlsClient` library availability for end-to-end tests.
- Existing multipart parity tests remain valid.

### Risks/Blockers

- Native cookie jar behavior may be required for behavior not represented by response headers.
- Final `TlsClient` request body may still need to be buffered/base64 encoded because of the native request contract.
- If native `TlsClient` does not expose a way to disable its jar per request/session, document the limitation instead of emulating partial behavior silently.

### Deliverables

- `TlsClient` cookie parsing behavior aligned with shared pipeline options.
- Missing multipart files fail consistently across transports.
- Reduced avoidable file upload memory overhead.

### Validation/Testing Criteria

- With `DisableCookieParsing = true`, a `Set-Cookie` response is not sent on the next `TlsClient` request through native jar side effects.
- Missing multipart file throws for `TlsClient` as it does for other transports.
- Existing binary/multipart parity tests pass when native library is available.

### Exit Criteria

- No known adapter-level cookie or multipart parity drift remains in the scoped areas.

## Phase 5. SystemNet Client Reuse

### Objective

Reduce per-request overhead for `HttpLibrary.SystemNet` by reusing safe `HttpClient` instances, gated behind parity tests.

### Scope

- `RuriLib/Functions/Http/HttpClientRequestHandler.cs`
- `RuriLib/Functions/Http/HttpFactory.cs`
- Optional small helper for client cache key generation.

### Technical Tasks

- Introduce a static bounded `HttpClient` or handler cache keyed by:
  - proxy type/host/port
  - proxy credentials
  - security protocol
  - custom cipher-suite settings
  - certificate revocation mode
  - connect/read-write timeouts
- Prefer `SocketsHttpHandler` where it can preserve current behavior and expose explicit SSL/connect options.
- Keep cookies manual by continuing to avoid shared `CookieContainer` state for block requests.
- Add cleanup/expiry for idle cached clients.
- Ensure per-request headers and content remain request-local.
- Treat this phase as optional if parity tests show proxy/TLS behavior drift that cannot be fixed surgically.

### Dependencies

- Phase 0 or Phase 2 should provide connection-count tests.
- Existing `HttpFactory` timeout normalization remains the source of truth.

### Risks/Blockers

- Connection reuse can affect servers that implicitly rely on new connections for every request.
- `HttpClientHandler` vs `SocketsHttpHandler` behavior may differ for proxy schemes and TLS options.
- Cached handlers can retain DNS/socket state longer than per-request clients; expiry must be explicit.

### Deliverables

- Reused `SystemNet` clients for equivalent transport settings.
- Cache cleanup logic.
- Tests proving no cookie/header leakage between requests.

### Validation/Testing Criteria

- Two equivalent `SystemNet` requests can reuse a connection when allowed.
- Different proxy credentials or TLS settings do not share the same cached client.
- Existing HTTP parity tests pass.

### Exit Criteria

- `SystemNet` no longer allocates and disposes a full `HttpClient` stack for every HTTP block request.
- If reuse is deferred, the reason and failing parity case are documented.

## Phase 6. Test Debt Reduction And Documentation

### Objective

Make HTTP tests deterministic and document the updated transport rules.

### Scope

- `RuriLib.Http.Tests/RLHttpClientTests.cs`
- `RuriLib.Http.Tests/ProxyClientHandlerTests.cs`
- `RuriLib.Http/README.md`
- Existing docs under `docs/`

### Technical Tasks

- Replace external `httpbin.org` and `nghttp2.org` dependencies with local server scenarios where practical.
- Keep any true external interoperability tests explicitly categorized and skipped by default if needed.
- Document:
  - which layer owns redirects
  - timeout precedence
  - connection reuse rules
  - logging redaction/truncation behavior
  - `TlsClient` native availability expectations.
- Decide whether stale or duplicate HTTP code should be marked obsolete in a later cleanup pass:
  - `RuriLib/Functions/Http/RLHttpClient.cs`
  - `RuriLib.Http/ProxyClientHandler.cs`

### Dependencies

- Behavior decisions from Phases 1-5.
- Local test server supports required response encodings or uses minimal deterministic fixtures.

### Risks/Blockers

- Some current tests may be integration smoke tests in disguise; replacing them requires preserving intent, not only assertions.
- Removing or obsoleting public types may be a separate compatibility decision.

### Deliverables

- Local deterministic transport tests.
- Updated README/docs for HTTP transport behavior.
- Follow-up cleanup list for stale public types, if not changed in this pass.

### Validation/Testing Criteria

- HTTP tests do not require public internet access for normal CI.
- Documentation matches implemented behavior.
- Full targeted validation passes:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
  - `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`

### Exit Criteria

- HTTP request behavior has executable regression coverage and current docs.
- Remaining non-implemented cleanup items are explicitly tracked.

## Edge Cases To Cover

- Multiple `Set-Cookie` headers and comma-containing cookie attributes.
- Duplicate response headers with different casing.
- `HEAD`, `204`, and `304` responses with no body.
- Redirect responses with bodies when auto-redirect is disabled or exhausted.
- HTTP/1.0 and HTTP/1.1 connection-close semantics.
- Chunked responses with trailing headers.
- Gzip, deflate, and brotli response bodies without double-decompression.
- Missing multipart files and restricted-CWD file checks.
- Cancellation during connect, send, header read, and body read.

## Security Considerations

- Redact sensitive HTTP headers in request, response, and exception context logs.
- Do not broaden TLS certificate bypass behavior.
- Keep `AllowHttpsToHttpRedirect` behavior unchanged in this plan unless separately requested.
- Treat proxy credentials as secret in pool keys/logs; pool identity may include credentials internally but logs must not emit them.

## Performance Considerations

- Primary performance wins come from safe connection/client reuse and avoiding log-only payload allocations.
- Connection pooling must be bounded and must not retain dead connections.
- Multipart improvements reduce avoidable allocations but cannot eliminate final buffering if the downstream native API requires a base64 string.

## Failure Handling

- Timeout failures should reflect configured request/read-write timeouts.
- Missing files in multipart requests should fail loud and consistently across transports.
- Pool acquisition under load must not recurse indefinitely or grow unbounded resources.
- Native `TlsClient` absence should produce deterministic skips in tests and clear runtime errors when selected by a config.

## Rollback Strategy

- Keep each phase as a separate change set.
- Roll back by transport:
  - Phase 1 changes can be isolated to `RuriLibHttp`.
  - Phase 2 keep-alive changes can be reverted while keeping Phase 1 safety fixes.
  - Phase 4 changes can be isolated to `TlsClientRequestHandler`.
  - Phase 5 changes can fall back to per-request `HttpClient` construction.
- Preserve regression tests where they encode intended behavior, even if implementation is rolled back and fixed differently.

## Recommended Execution Order

1. Phase 0: Baseline and deterministic test harness.
2. Phase 1: `RuriLibHttp` safety fixes.
3. Phase 2: gated `RuriLibHttp` keep-alive enablement.
4. Phase 3: HTTP logging safety and bounded previews.
5. Phase 4: `TlsClient` adapter parity.
6. Phase 5: gated `SystemNet` client reuse.
7. Phase 6: test debt reduction and documentation.

Reasoning: fix correctness and safety before broader performance behavior changes. Keep-alive and `SystemNet` pooling are high-ROI, but both can change wire behavior, so they are gated behind deterministic connection-reuse and parity tests.

## Definition Of Done

- Planned phases either implemented or explicitly deferred.
- No HTTP behavior change lands without focused regression coverage.
- `SystemNet`, `RuriLibHttp`, and available `TlsClient` tests pass for scoped behavior.
- Logs are bounded and redact sensitive HTTP values.
- Connection/client pools are bounded and do not reuse known-closed resources.
