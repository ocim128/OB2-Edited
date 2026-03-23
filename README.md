# Flux

Flux is a .NET 8 automation platform built around `RuriLib`, with desktop, shared orchestration, and web/API layers in the same repository.

This root README is optimized for fast orientation. If you are trying to understand behavior, start from execution flow and project boundaries before reading individual files.

## Repository Layout

- `RuriLib/`
  Core automation/runtime library. Blocks, config execution, jobs, bot state, providers, scripting.
- `RuriLib.Http/`
  Custom HTTP transport used by `HttpLibrary.RuriLibHttp`.
- `RuriLib.Proxies/`
  TCP/proxy abstraction used by `RuriLib.Http`.
- `RuriLib.Parallelization/`
  Parallel job execution primitives.
- `Flux.Shared/`
  Shared application services, job orchestration, DTO projection, notifications.
- `Flux.Core/`
  Persistence/domain layer.
- `Flux.Native/`
  WPF desktop application.
- `Flux.Web/`
  ASP.NET web/API layer.
- `flux-web-client/`
  Angular frontend.
- `Libraries/TlsClient.NET/`
  Included TLS client transport used by `HttpLibrary.TlsClient`.

## Execution Flows

### HTTP request block

The request block flow is:

1. `RuriLib/Blocks/Requests/Http/Methods.cs`
2. `RuriLib/Functions/Http/HttpRequestHandler.cs`
3. one of:
   `HttpClientRequestHandler`
   `RLHttpClientRequestHandler`
   `TlsClientRequestHandler`
4. `RuriLib/Functions/Http/HttpFactory.cs`
5. lower transport code in `RuriLib.Http/`, `RuriLib.Proxies/`, or `Libraries/TlsClient.NET/`

Transport mapping:

- `SystemNet` -> built-in `HttpClient`
- `RuriLibHttp` -> custom transport in `RuriLib.Http`
- `TlsClient` -> `TlsClient.NET`

### Job runtime

The main runtime path is:

1. `RuriLib/Models/Jobs/MultiRunJob.cs`
2. `RuriLib/Models/Jobs/JobInitializer.cs`
3. `RuriLib/Models/Jobs/JobLifecycleService.cs`
4. `RuriLib/Models/Jobs/Execution/`
5. `Flux.Shared/Services/JobOrchestrator.cs`
6. `Flux.Shared/Services/JobProjectionService.cs`
7. `Flux.Shared/Services/JobEventSubscriptionService.cs`

### Config execution

For debugger and generated execution behavior, start here:

1. `RuriLib/Models/Debugger/ConfigDebugger.cs`
2. `RuriLib/Helpers/Transpilers/`
3. `RuriLib/Models/Blocks/`
4. `RuriLib/Blocks/`

## Good Starting Points By Problem

### "HTTP request is failing"

Read:

- `RuriLib/Blocks/Requests/Http/Methods.cs`
- `RuriLib/Functions/Http/HttpRequestHandler.cs`
- `RuriLib/Functions/Http/HttpFactory.cs`
- the concrete transport handler

### "Job start/stop/progress is wrong"

Read:

- `RuriLib/Models/Jobs/MultiRunJob.cs`
- `RuriLib/Models/Jobs/JobInitializer.cs`
- `RuriLib/Models/Jobs/JobLifecycleService.cs`
- `Flux.Shared/Services/JobOrchestrator.cs`

### "A block does the wrong thing"

Read:

- block instance/descriptor under `RuriLib/Models/Blocks/`
- implementation under `RuriLib/Blocks/`
- transpiler glue under `RuriLib/Helpers/Transpilers/` if needed

If the issue is block registration or discovery, also read:

- `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs`
- `RuriLib/Helpers/Blocks/DescriptorsRepository.cs`
- `RuriLib/Helpers/Blocks/BlockFactory.cs`

If the issue is custom block LC parsing/serialization or generated C#, start from:

- `RuriLib/Models/Blocks/Custom/HttpRequestBlockInstance.cs`
- `RuriLib/Models/Blocks/Custom/HttpRequestBlockInstance.LoliCode.cs`
- `RuriLib/Models/Blocks/Custom/HttpRequestBlockInstance.CSharp.cs`
- `RuriLib/Models/Blocks/Custom/ParseBlockInstance.cs`
- `RuriLib/Models/Blocks/Custom/ParseBlockInstance.LoliCode.cs`
- `RuriLib/Models/Blocks/Custom/ParseBlockInstance.CSharp.cs`

### "Desktop UI issue"

Read:

- `Flux.Native/ViewModels/`
- `Flux.Native/Views/`
- then whatever service the UI delegates into

For the recently split desktop pages, start from the shell page and then drill into feature controls/viewmodels:

- tools dashboard shell: `Flux.Native/Views/Pages/Tools/Monitor.xaml`
- tools dashboard state: `Flux.Native/ViewModels/Pages/ToolsPageViewModel.cs`
- individual tools: `Flux.Native/ViewModels/Tools/` and `Flux.Native/Views/Controls/Tools/`
- config stacker shell: `Flux.Native/Views/Pages/ConfigPages/ConfigStacker.xaml`
- config stacker features: `Flux.Native/ViewModels/Config/` and `Flux.Native/Views/Controls/Config/`

## Recent Runtime Refactors

The codebase already contains a few important simplifications:

- `MultiRunJob` no longer owns all initialization and lifecycle logic directly
- `JobOrchestrator` no longer owns all projection and subscription logic directly
- desktop job create/edit/clone/delete and job option loading now flow through `Flux.Shared/Services/JobOrchestrator.cs` instead of `Flux.Native`
- HTTP transport behavior is centered around a shared request pipeline in `HttpRequestHandler`
- built-in block registration now goes through `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs`; reflection-based descriptor discovery is now mainly the plugin path
- `BlockDescriptor` now owns block instance creation through a registered factory, so `BlockFactory` stays thin
- Legacy `HttpCloak` HTTP block settings are normalized to `TlsClient` during load/transpile
- `HttpRequestBlockInstance` and `ParseBlockInstance` are split into core state, LoliCode parsing/serialization, and C# emission partials
- Puppeteer browser blocks now mirror the Playwright browser split across `Methods.cs`, `Methods.Helpers.cs`, `Methods.Cleanup.cs`, and `Methods.Stealth.cs`
- Runtime/global settings and config-scoped settings now use distinct model names: `GlobalGeneralSettings`/`GlobalProxySettings` and `ConfigGeneralSettings`/`ConfigProxySettings`
- `Monitor` and `ConfigStacker` in `Flux.Native` are now shell pages with feature-specific controls/viewmodels
- `Home` is now a thin page over `Flux.Native/ViewModels/Pages/HomeViewModel.cs`, with dashboard data coming from `Flux.Shared/Abstractions/IDashboardService.cs`

If you are debugging older assumptions about these areas, verify the current extracted services first.

## Diagnostics

### HTTP diagnostics

HTTP failures now log:

- transport name
- exception chain
- request context
- effective timeout/proxy settings
- stack trace when verbose mode is enabled, and for important framework argument/state errors

The logging is implemented in:

- `RuriLib/Functions/Http/HttpRequestHandler.cs`

### Verbose mode

Verbose mode is useful when reading runtime behavior or debugging config execution.

Relevant files:

- `RuriLib/Models/Settings/GlobalGeneralSettings.cs`
- `RuriLib/Providers/General/`
- `RuriLib/Models/Debugger/ConfigDebugger.cs`

## Build And Test

From the repository root:

```powershell
dotnet restore Flux.sln
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

Frontend:

```powershell
cd flux-web-client
npm install
npm run build
```

## Search Shortcuts

Use `rg`.

Examples:

```powershell
rg -n "HttpLibrary" RuriLib
rg -n "ExecutePipelineAsync" RuriLib/Functions/Http
rg -n "MultiRunJob" RuriLib Flux.Shared Flux.Native
rg -n "ConfigDebugger" RuriLib
rg -n "OnError" RuriLib Flux.Shared Flux.Native
```

## Notes

- `UserData/` contains local runtime data and is usually not the place to start for source changes.
- `packages/` is a local NuGet cache.
- `Changelog/` contains version history.
- `Libraries/TlsClient.NET/` is lower-level transport code; only go there after confirming the bug is in the TLS transport path.

For agent-specific working guidance, see [AGENTS.md](/c:/Users/user/Documents/Repo/OB2-Edited/AGENTS.md).
