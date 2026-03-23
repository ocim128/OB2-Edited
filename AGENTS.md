# Flux Agent Guide

This file is for AI agents and code assistants working in this repository.
It is intentionally biased toward fast codebase navigation, common execution paths, and low-friction debugging.

## Scope

- Main solution: `Flux.sln`
- Main backend/runtime code: `RuriLib/`, `RuriLib.Http/`, `RuriLib.Proxies/`, `Flux.Core/`, `Flux.Shared/`, `Flux.Native/`
- Frontend: `flux-web-client/`
- Web API: `Flux.Web/`
- Vendored TLS library: `Libraries/TlsClient.NET/`

If a task only concerns runtime logic, jobs, blocks, proxies, or HTTP, start in `RuriLib/` and `Flux.Shared/`.

## Fast Project Map

- `RuriLib/`
  Core automation/runtime library. Contains blocks, config execution, jobs, bot state, providers, settings, and scripting integration.
- `RuriLib.Http/`
  Custom HTTP transport implementation used by `HttpLibrary.RuriLibHttp`.
- `RuriLib.Proxies/`
  TCP/proxy connection layer used by `RuriLib.Http`.
- `Flux.Shared/`
  Shared orchestration and DTO layer used by the application shell around `RuriLib`.
- `Flux.Core/`
  Persistence/domain layer for jobs, configs, hits, and repositories.
- `Flux.Native/`
  WPF desktop app. Many user-facing flows end here, but execution/runtime behavior usually delegates into `RuriLib` and `Flux.Shared`.
- `Flux.Web/`
  ASP.NET API layer. Touch only when the task is explicitly about the web API.
- `flux-web-client/`
  Angular frontend.
- `Libraries/TlsClient.NET/`
  External-but-included TLS transport. Useful when debugging `HttpLibrary.TlsClient`.

## High-Signal Entry Points

### HTTP request block flow

1. `RuriLib/Blocks/Requests/Http/Methods.cs`
2. `RuriLib/Functions/Http/*RequestHandler.cs`
3. `RuriLib/Functions/Http/HttpRequestHandler.cs`
4. `RuriLib/Functions/Http/HttpFactory.cs`
5. `RuriLib.Http/` and `RuriLib.Proxies/` if the issue is transport-level

Transport selection:
- `SystemNet` -> `HttpClientRequestHandler`
- `RuriLibHttp` -> `RLHttpClientRequestHandler`
- `TlsClient` -> `TlsClientRequestHandler`

### Job runtime flow

1. `RuriLib/Models/Jobs/MultiRunJob.cs`
2. `RuriLib/Models/Jobs/JobInitializer.cs`
3. `RuriLib/Models/Jobs/JobLifecycleService.cs`
4. `RuriLib/Models/Jobs/Execution/`
5. `Flux.Shared/Services/JobOrchestrator.cs`
6. `Flux.Shared/Services/JobProjectionService.cs`
7. `Flux.Shared/Services/JobEventSubscriptionService.cs`

### Config execution / debugging flow

1. `RuriLib/Models/Debugger/ConfigDebugger.cs`
2. `RuriLib/Helpers/Transpilers/`
3. `RuriLib/Models/Blocks/`
4. `RuriLib/Blocks/`

### Bot state / providers

- `RuriLib/Models/Bots/BotData.cs`
- `RuriLib/Models/Bots/Providers.cs`
- `RuriLib/Providers/*`
- `RuriLib/Services/RuriLibSettingsService.cs`

## Where To Start By Task

### "HTTP block is broken"

Read in this order:
1. `RuriLib/Blocks/Requests/Http/Methods.cs`
2. `RuriLib/Functions/Http/HttpRequestHandler.cs`
3. the selected concrete handler
4. `RuriLib/Functions/Http/HttpFactory.cs`
5. `RuriLib.Http/` or `Libraries/TlsClient.NET/` if needed

### "Job start/stop/progress is broken"

Read in this order:
1. `RuriLib/Models/Jobs/MultiRunJob.cs`
2. `RuriLib/Models/Jobs/JobInitializer.cs`
3. `RuriLib/Models/Jobs/JobLifecycleService.cs`
4. `Flux.Shared/Services/JobOrchestrator.cs`
5. `Flux.Shared/Services/JobProjectionService.cs`

### "A block behavior is wrong"

Read in this order:
1. block descriptor / block instance under `RuriLib/Models/Blocks/`
2. implementation under `RuriLib/Blocks/`
3. transpiler glue under `RuriLib/Helpers/Transpilers/` if the block is emitted into generated C#

### "Desktop UI issue"

Read in this order:
1. viewmodel under `Flux.Native/ViewModels/`
2. view or code-behind under `Flux.Native/Views/`
3. delegated service in `Flux.Shared/` or `RuriLib/`

For recently split `Flux.Native` pages, start from the shell page and then move into feature controls/viewmodels:
- Tools dashboard shell: `Flux.Native/Views/Pages/Tools/Monitor.xaml` and `Monitor.xaml.cs`
- Tools dashboard state: `Flux.Native/ViewModels/Pages/ToolsPageViewModel.cs`
- Individual tool cards: `Flux.Native/ViewModels/Tools/` and `Flux.Native/Views/Controls/Tools/`
- Config stacker shell: `Flux.Native/Views/Pages/ConfigPages/ConfigStacker.xaml` and `ConfigStacker.xaml.cs`
- Config stacker block list / inspector: `Flux.Native/Views/Controls/Config/`
- Config stacker state: `Flux.Native/ViewModels/Config/ConfigStackerViewModel.cs`, `ConfigStackerViewModel.Tooling.cs`, and `ConfigStackerInspectorViewModel.cs`

## Common Architecture Notes

- `RuriLib` is the execution engine.
- `Flux.Shared` is the orchestration/projection layer around running jobs.
- `Flux.Native` is often UI glue over services from `Flux.Shared` and `RuriLib`.
- `RuriLib.Http` and `RuriLib.Proxies` are lower-level infrastructure, not block-level APIs.
- `TlsClient.NET` is effectively a separate transport stack.

Recent runtime simplifications already in place:
- Job initialization and lifecycle were extracted from `MultiRunJob`.
- Job projection and event subscriptions were extracted from `JobOrchestrator`.
- Desktop job create/edit/clone/delete and job option loading now go through `Flux.Shared/Services/JobOrchestrator.cs`; `Flux.Native` jobs UI should not persist jobs or deserialize job options directly.
- HTTP request execution is now centered on shared pipeline logic in `HttpRequestHandler`.
- Legacy `HttpCloak` HTTP block settings now normalize to `TlsClient` during load/transpile.
- Puppeteer browser blocks now mirror the Playwright browser split across `RuriLib/Blocks/Puppeteer/Browser/Methods.cs`, `Methods.Helpers.cs`, `Methods.Cleanup.cs`, and `Methods.Stealth.cs`.
- Runtime/global settings and config-scoped settings now use distinct model names: `GlobalGeneralSettings`/`GlobalProxySettings` and `ConfigGeneralSettings`/`ConfigProxySettings`.

Recent desktop refactors already in place:
- `Monitor` is now a shell page; tool-specific behavior lives in dedicated controls/viewmodels.
- `ConfigStacker` is now a shell page; block list and inspector behavior are split into dedicated controls/viewmodels.
- `Home` is now a thin page over `Flux.Native/ViewModels/Pages/HomeViewModel.cs`, with dashboard data pulled through `Flux.Shared/Abstractions/IDashboardService.cs`.

## Logging And Debugging

### HTTP

The shared HTTP pipeline now logs transport-aware failure context from `RuriLib/Functions/Http/HttpRequestHandler.cs`.
When debugging request failures, check for:
- transport name
- exception chain
- request context
- proxy/connect/read-write timeout settings
- stack trace when verbose mode is enabled, or for framework argument/state errors

### Verbose mode

Verbose mode is controlled through:
- `RuriLib/Models/Settings/GlobalGeneralSettings.cs`
- provider access via `RuriLib/Providers/General/`

It increases diagnostic output for config debugging and runtime failures.

## Build And Test

Run from repo root:

```powershell
dotnet build Flux.sln -c Debug
dotnet test Flux.sln
```

Useful targeted commands:

```powershell
dotnet build RuriLib/RuriLib.csproj -c Debug --no-restore
dotnet build Flux.Shared/Flux.Shared.csproj -c Debug --no-restore
dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj
dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj
```

For frontend:

```powershell
cd flux-web-client
npm install
npm run build
```

## Search Tips

Prefer `rg`.

Examples:

```powershell
rg -n "HttpLibrary" RuriLib
rg -n "ExecutePipelineAsync" RuriLib/Functions/Http
rg -n "MultiRunJob" RuriLib Flux.Shared Flux.Native
rg -n "OnError" RuriLib Flux.Shared Flux.Native
rg -n "ConfigDebugger" RuriLib
```

## Change Safety

- Read the whole execution path before refactoring shared runtime code.
- Changes in `HttpRequestHandler`, `HttpFactory`, `MultiRunJob`, or `JobOrchestrator` have large blast radius.
- Prefer adding diagnostics before building one-off reproduction programs.
- Avoid changing vendored `Libraries/TlsClient.NET/` unless the bug is clearly in that transport.
- If a behavior differs between `SystemNet`, `RuriLibHttp`, and `TlsClient`, compare the three handlers first before going lower.

## Files Worth Knowing

- `RuriLib/Blocks/Requests/Http/Methods.cs`
- `RuriLib/Functions/Http/HttpRequestHandler.cs`
- `RuriLib/Functions/Http/HttpFactory.cs`
- `RuriLib/Models/Bots/BotData.cs`
- `RuriLib/Models/Debugger/ConfigDebugger.cs`
- `RuriLib/Models/Jobs/MultiRunJob.cs`
- `RuriLib/Models/Jobs/JobInitializer.cs`
- `RuriLib/Models/Jobs/JobLifecycleService.cs`
- `Flux.Shared/Services/JobOrchestrator.cs`
- `Flux.Shared/Services/JobProjectionService.cs`
- `Flux.Shared/Services/JobEventSubscriptionService.cs`

## Non-Code Directories

- `UserData/`
  Local runtime data, configs, wordlists, and outputs. Usually not source-controlled.
- `Changelog/`
  Version history.
- `packages/`
  Local NuGet cache.

Keep this file practical. Prefer updating entry points and debugging guidance over adding broad marketing-style project descriptions.
