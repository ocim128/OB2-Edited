# HTTP Transport Remediation Plan

## Purpose
This document turns the current HTTP transport audit into an execution plan for:

- `HttpLibrary.SystemNet`
- `HttpLibrary.RuriLibHttp`
- `HttpLibrary.TlsClient`

The goal is to remove behavior drift, fix confirmed defects, and add regression coverage so future changes do not reopen the same issues.

## Goals
- Make transport behavior consistent unless a difference is explicit and documented.
- Restore or clarify the actual meaning of `HttpLibrary.RuriLibHttp`.
- Fix shared redirect semantics and response-body handling.
- Make `TlsClient` safe for binary responses and multipart uploads.
- Fix pooling/configuration isolation issues in the `RuriLibHttp` path.
- Add missing coverage across all three transports.

## Non-Goals
- Rewriting vendored `Libraries/TlsClient.NET` unless the adapter layer cannot solve the issue.
- Redesigning the block API or changing unrelated runtime/job behavior.

## Confirmed Findings To Address
1. `HttpLibrary.RuriLibHttp` currently routes through `RuriLib/Functions/Http/RLHttpClient.cs`, which wraps `System.Net.Http.HttpClient`, instead of using the real custom transport in `RuriLib.Http/RLHttpClient.cs`.
2. Redirect handling is incorrect for 307/308 and discards all 3xx bodies even when `ReadResponseContent = true`.
3. `TlsClient` response handling reconstructs response bytes through UTF-8 text, which corrupts binary and non-UTF8 payloads.
4. `TlsClient` multipart construction is text-based and corrupts raw/file parts.
5. The static `RuriLibHttp` pooled-client key is too coarse and can reuse the wrong handler configuration. Pool accounting also leaks active-client count in some disposal paths.
6. `allowHttpsToHttpRedirect` exists in block settings but is not emitted by the C# transpiler, so transpiled configs never pass the value through.
7. Existing high-level HTTP regression tests only cover `SystemNet` and `RuriLibHttp`; `TlsClient` is effectively uncovered.

## Workstreams

### 1. Restore The Real `RuriLibHttp` Transport
Recommended direction:

- Keep `HttpLibrary.RuriLibHttp` as the custom socket-based transport.
- Stop using the wrapper in `RuriLib/Functions/Http/RLHttpClient.cs` for that enum value.
- Use `RuriLib.Http.RLHttpClient` directly from `HttpFactory` and `RLHttpClientRequestHandler`.

Files to change:

- `RuriLib/Functions/Http/HttpFactory.cs`
- `RuriLib/Functions/Http/RLHttpClientRequestHandler.cs`
- `RuriLib/Functions/Http/RLHttpClient.cs`
- `RuriLib.Http/RLHttpClient.cs`

Implementation notes:

- Either remove the wrapper in `RuriLib/Functions/Http/RLHttpClient.cs` or rename it so it no longer implies it is the custom transport.
- `AbsoluteUriInFirstLine` only makes sense if the request reaches the custom writer in `RuriLib.Http`.
- Keep the shared pipeline in `HttpRequestHandler`; only the concrete send path should change.

Acceptance criteria:

- `HttpLibrary.RuriLibHttp` uses the custom transport stack again.
- `AbsoluteUriInFirstLine` produces a different request line when enabled on the custom transport.
- Transport naming in logs and code matches actual behavior.

### 2. Fix Shared Redirect Semantics
This is a shared-pipeline issue and should be fixed once for all transports.

Files to change:

- `RuriLib/Functions/Http/HttpRedirectPolicy.cs`
- `RuriLib/Functions/Http/HttpRequestModels.cs`
- `RuriLib/Functions/Http/HttpRequestHandler.cs`
- `RuriLib/Functions/Http/HttpClientRequestHandler.cs`
- `RuriLib/Functions/Http/RLHttpClientRequestHandler.cs`
- `RuriLib/Functions/Http/TlsClientRequestHandler.cs`
- `RuriLib/Functions/Http/HttpResponseMapper.cs`

Required behavior:

- Preserve method and body for 307 and 308.
- Rewrite to `GET` for 303, and for 301/302 only when matching existing intended browser-like behavior.
- Preserve required request headers when the redirect keeps the original method/body.
- Only carry `Authorization` when the redirect target is still allowed by policy.
- If auto-redirect is disabled, or redirect limit is exhausted, preserve the 3xx response body when `ReadResponseContent = true`.

Implementation notes:

- `NormalizedHttpRequest.CreateRedirect` cannot always hardcode `GET`.
- Redirect creation likely needs the response status code so it can choose method/body/header behavior.
- Preserve cookies via the existing cookie jar, but do not silently strip request content for 307/308.

Acceptance criteria:

- POST + 307/308 reaches the redirected endpoint as POST with the original payload.
- 3xx response body is available when redirects are not followed.
- HTTPS to HTTP redirect behavior still respects `AllowHttpsToHttpRedirect`.

### 3. Make `TlsClient` Response Handling Byte-Safe
Files to change:

- `RuriLib/Functions/Http/TlsClientRequestHandler.cs`

Required behavior:

- Request byte responses when `ReadResponseContent = true`.
- Decode the returned base64 payload into the original raw bytes.
- Do not round-trip response bodies through `Encoding.UTF8.GetBytes(...)`.
- Preserve header-only behavior when `ReadResponseContent = false`.

Implementation notes:

- Align with the byte-response pattern already used by `Libraries/TlsClient.NET/src/Providers/TlsClient.Provider.HttpClient/TlsClientHandler.cs`.
- `HttpResponseMapper` expects `RawBody` to be the original bytes so `CodePagesEncoding` can work correctly afterward.

Acceptance criteria:

- Binary payloads survive unchanged.
- Non-UTF8 text payloads decode correctly when `CodePagesEncoding` is set.
- `TlsClient` behavior matches the other transports for `ReadResponseContent`.

### 4. Make `TlsClient` Multipart Byte-Safe
Files to change:

- `RuriLib/Functions/Http/TlsClientRequestHandler.cs`
- Possibly shared multipart helpers in `RuriLib/Functions/Http/HttpRequestHandler.cs` if extracting common byte-building logic is cleaner.

Required behavior:

- Build multipart bodies as bytes, not strings.
- Treat `RawHttpContent` as bytes.
- Read file parts as bytes, not text.
- Keep boundary and per-part headers intact.
- Send the multipart body as a byte request through `TlsClient`.

Implementation notes:

- `TlsClient` still needs the final request body serialized into its request model, so the adapter may still need to buffer the full multipart payload in memory.
- That is acceptable for parity with the current design, but the payload must be byte-accurate.

Acceptance criteria:

- Binary files uploaded through `TlsClient` arrive unchanged.
- Raw multipart parts preserve exact bytes.
- Multipart behavior matches `SystemNet` and `RuriLibHttp` for equivalent inputs.

### 5. Fix `RuriLibHttp` Client Pool Isolation And Accounting
Files to change:

- `RuriLib/Functions/Http/RLHttpClientRequestHandler.cs`
- Possibly `RuriLib/Functions/Http/HttpFactory.cs` if configuration ownership moves.

Required behavior:

- Pool keys must include all handler-affecting options that can change behavior.
- At minimum include:
  - proxy type/host/port
  - proxy credentials
  - security protocol
  - custom cipher-suite usage and exact suite list
  - response-read mode if that remains client-level state
  - any other per-client TLS/handler settings that alter behavior
- Decrement active-client count on every disposal path that permanently removes a client.

Recommended hardening:

- Extract pooled-client key generation into a small helper that can be unit tested directly.
- Consider moving request-specific flags off the pooled client if they do not belong to connection identity.
- Tighten the `ActiveClients` increment path so the pool limit is not advisory under concurrency.

Acceptance criteria:

- Two requests that differ in proxy credentials or transport options do not accidentally share a pooled client.
- Expired clients do not leave stale active counts behind.
- Pool size remains bounded during repeated create/expire cycles.

### 6. Fix Block/Transpiler Option Wiring
Files to change:

- `RuriLib/Models/Blocks/Custom/HttpRequestBlockInstance.CSharp.cs`

Required behavior:

- Emit `AllowHttpsToHttpRedirect` into generated `HttpRequestOptions`.

Validation notes:

- `FromLC` and descriptor parsing already expose the setting; the confirmed gap is in C# emission.
- Add a regression test through the debugger/transpiler path, not only a low-level unit test.

Acceptance criteria:

- A transpiled config with `allowHttpsToHttpRedirect = true` can follow HTTPS to HTTP redirects.
- A transpiled config with the default value still blocks the downgrade redirect.

## Coverage Plan

### A. Upgrade The Test Server Fixture
The current `RuriLib.Tests/Infrastructure/TestHttpServer.cs` only supports a fixed 200 OK text response and cannot verify request semantics.

Replace or extend it into a programmable fixture that can:

- register per-request handlers
- inspect method, path, headers, and raw body bytes
- emit arbitrary status codes, headers, and raw body bytes
- emit redirects with bodies
- emit multiple `Set-Cookie` headers
- capture the first request line for `AbsoluteUriInFirstLine`

Preferred location:

- `RuriLib.Tests/Infrastructure/TestHttpServer.cs`

Optional companion types:

- `RecordedHttpRequest`
- `TestHttpResponse`
- `HttpTestScenario`

### B. Shared Parity Tests Across All Transports
Add a new high-level test suite that exercises the block/runtime path against all three transports.

Preferred location:

- `RuriLib.Tests/Functions/Http/HttpTransportParityTests.cs`

Parameterize on:

- `HttpLibrary.SystemNet`
- `HttpLibrary.RuriLibHttp`
- `HttpLibrary.TlsClient`

Minimum cases:

1. basic 200 text response
2. duplicate header casing does not throw
3. 3xx body is preserved when auto-redirect is off
4. 307 preserves method and body
5. 308 preserves method and body
6. HTTPS to HTTP redirect is blocked by default
7. HTTPS to HTTP redirect is allowed when the option is enabled
8. cookies set on one response are available on the next request
9. binary response round-trips correctly
10. non-UTF8 response plus `CodePagesEncoding` decodes correctly
11. multipart upload with binary file and raw bytes

### C. Keep The Existing Debugger Coverage, But Expand It
Extend:

- `RuriLib.Tests/Debugger/ConfigDebuggerHttpRequestTests.cs`

Add:

- `TlsClient` to the smoke-test matrix when the native runtime is available.
- A transpiler-level regression for `AllowHttpsToHttpRedirect`.

Platform handling:

- If the test host does not have the native `tls-client.dll`, skip `TlsClient` tests with a clear reason.
- If CI is Windows x64 and the package output is available, run them by default.

### D. Transport-Level Tests For `RuriLib.Http`
If workstream 1 restores the real custom transport, add lower-level tests that target the custom transport directly.

Preferred location:

- `RuriLib.Http.Tests/`

Minimum cases:

1. request line uses absolute URI when configured
2. redirect handling remains disabled at the transport layer when the shared pipeline owns redirects
3. binary response body is preserved
4. connection/pool cleanup does not leak state after repeated requests

### E. Pooling Tests
Add tests that directly validate pooled-client identity and cleanup.

Preferred location:

- `RuriLib.Tests/Functions/Http/RLHttpClientPoolingTests.cs`

Recommended approach:

- Extract key generation and maybe pool-entry lifecycle into helpers small enough to test directly.
- Verify different proxy credentials and cipher lists produce different keys.
- Verify disposing expired clients decrements the active count.

## Recommended Execution Order

### Phase 0. Build The Test Harness First
- Upgrade `TestHttpServer`.
- Add the shared parity test skeleton.
- Add a `TlsClient` availability helper for tests.

### Phase 1. Fix Shared Semantics
- Redirect semantics
- 3xx body preservation
- Transpiler pass-through for `AllowHttpsToHttpRedirect`

### Phase 2. Restore `RuriLibHttp`
- Rewire `HttpLibrary.RuriLibHttp` to the real custom transport.
- Add direct transport tests in `RuriLib.Http.Tests`.

### Phase 3. Fix `TlsClient`
- Byte-safe response handling
- Byte-safe multipart handling
- Add parity coverage for binary and multipart cases

### Phase 4. Harden Pooling
- Expand key identity
- Fix accounting
- Add pooling-specific tests

### Phase 5. Full Validation
- `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
- `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`
- `dotnet test Flux.sln`

## Definition Of Done
- All confirmed findings are fixed in code.
- `SystemNet`, `RuriLibHttp`, and `TlsClient` pass the shared parity suite for supported scenarios.
- `TlsClient` tests are either running in CI or explicitly skipped with deterministic environment checks.
- The debugger/transpiler path has regression coverage for `AllowHttpsToHttpRedirect`.
- The custom `RuriLib.Http` transport has direct tests once it is restored to active use.

## Risks And Guardrails
- Do not modify `Libraries/TlsClient.NET` unless the adapter layer in `RuriLib/Functions/Http/TlsClientRequestHandler.cs` cannot solve the issue.
- Redirect fixes have broad blast radius because they sit in the shared pipeline.
- Restoring the real `RuriLibHttp` transport may surface old transport-specific bugs; parity tests should land before the refactor is considered complete.
- `TlsClient` native mode may behave differently under concurrency; keep tests narrow and deterministic.
