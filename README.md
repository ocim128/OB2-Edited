# Flux

Flux is a Windows desktop automation platform for building and running configs against lists of input data. The execution engine (`RuriLib`) is independent of the UI; the WPF app (`Flux.Native`) is a shell over it, with persistence in `Flux.Core` and job orchestration in `Flux.Shared`.

- Runtime: .NET 8 (`net8.0` libraries, `net8.0-windows` WPF app)
- SDK: pinned to **8.0.401** by `global.json`
- Current version: `0.3.2` (`version.txt`), history in `Changelog/`
- Platform: Windows only, `win-x64` by default

## Repository Layout

| Path | What lives there |
|---|---|
| `RuriLib/` | Execution engine: blocks, configs, transpilers, scripting, jobs, bot state, providers, HTTP dispatch |
| `RuriLib.Http/` | Custom HTTP/1.1 client used by `HttpLibrary.RuriLibHttp` |
| `RuriLib.Proxies/` | TCP/proxy layer used by `RuriLib.Http` |
| `RuriLib.Parallelization/` | Bot/work-item parallelizer (pause, resume, stop, abort, CPM) |
| `Flux.Core/` | EF Core SQLite persistence, repositories, config/wordlist/proxy/job services |
| `Flux.Shared/` | `JobOrchestrator`, DTO projection, dashboard service |
| `Flux.Native/` | WPF desktop app and DI composition root |
| `RuriLib.Tests/`, `RuriLib.Http.Tests/`, `Flux.Shared.Tests/` | xUnit test projects |
| `Libraries/TlsClient.NET/` | Vendored TLS-fingerprint transport (nested git repo, locally patched) |
| `docs/` | Architecture, development, UI and Playwright documentation |
| `Changelog/` | Per-version release notes, inherited from upstream OpenBullet. Entries that mention the web app or Windows Forms describe upstream history, not the current code. |

`UserData/` (local configs and settings) and `packages/` (NuGet cache redirected by `nuget.config`) are runtime artifacts, not source.

## Quick Start

```powershell
dotnet restore Flux.sln
dotnet build Flux.sln -c Debug
dotnet test RuriLib.Tests/RuriLib.Tests.csproj
```

Run the desktop app through the publish-and-launch script (this is the normal local loop):

```powershell
Flux.Native\test_build\run_testing.bat
```

Full setup, test layout, migrations, and troubleshooting: [docs/development.md](docs/development.md).

## How Execution Flows

### Config → C#

A config is `Stack`, `LoliCode`, `CSharp`, or `DLL`. `ScriptPreparationService` converts it to C# (fast path for production, block-stack path for step-by-step debugging), then `ScriptBuilder` compiles it with Roslyn and caches the assembly in `UserData/CompiledScripts`.

1. `RuriLib/Models/Scripting/ScriptPreparationService.cs`
2. `RuriLib/Helpers/Transpilers/`
3. `RuriLib/Helpers/CSharp/ScriptBuilder.cs`

### Job execution

1. `Flux.Shared/Services/JobOrchestrator.cs` (commands, DTO projection)
2. `Flux.Core/Services/JobFactoryService.cs`, `JobManagerService.cs` (build + own live jobs)
3. `RuriLib/Models/Jobs/MultiRunJob.cs`
4. `RuriLib/Models/Jobs/JobInitializer.cs`, `JobLifecycleService.cs`
5. `RuriLib/Models/Jobs/Execution/` (`WorkItemFactory`, `JobResultProcessor`, `JobResourceScope`, execution handlers)

### HTTP request

1. `RuriLib/Blocks/Requests/Http/Methods.cs` (transport switch)
2. `RuriLib/Functions/Http/HttpRequestHandler.cs` (shared pipeline: normalize, log, timeout, redirect, map)
3. `HttpClientRequestHandler` / `RLHttpClientRequestHandler` / `TlsClientRequestHandler`
4. `RuriLib/Functions/Http/HttpFactory.cs`, then `RuriLib.Http/` or `Libraries/TlsClient.NET/`

Transport mapping: `SystemNet` → `HttpClient`, `RuriLibHttp` → `RuriLib.Http`, `TlsClient` → `TlsClient.NET`.

## Where To Start By Problem

| Problem | Start here |
|---|---|
| "HTTP request fails" | `Blocks/Requests/Http/Methods.cs` → `Functions/Http/HttpRequestHandler.cs` → the concrete handler |
| "Job start/stop/progress is wrong" | `Models/Jobs/MultiRunJob.cs` → `JobInitializer` → `JobLifecycleService` → `Flux.Shared/Services/JobOrchestrator.cs` |
| "A block does the wrong thing" | block instance under `RuriLib/Models/Blocks/` → implementation under `RuriLib/Blocks/` → transpiler glue |
| "Block registration or discovery is wrong" | `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs`, `DescriptorsRepository.cs` |
| "Desktop UI issue" | viewmodel under `Flux.Native/ViewModels/` → view under `Flux.Native/Views/` → delegated service |
| "Config editor / debugger behavior" | [docs/config-menu.md](docs/config-menu.md) |
| "Browser automation" | [docs/playwright.md](docs/playwright.md) |

## Documentation

| Document | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Layer boundaries, config/job/HTTP pipelines, block model, persistence, deliberate duplication, gotchas |
| [docs/development.md](docs/development.md) | Prerequisites, build/run/test commands, test conventions, migrations, validation, troubleshooting |
| [docs/config-menu.md](docs/config-menu.md) | Desktop config pages, editor shell, stacker, LoliCode/C# views, debugger |
| [docs/playwright.md](docs/playwright.md) | Playwright version pin, launch/cleanup flow, stealth layer, known limitations |
| [docs/lolicode-snippets.md](docs/lolicode-snippets.md) | Example LoliCode blocks for exercising the parser and debugger |
| [AGENTS.md](AGENTS.md) | Working rules and navigation for AI coding agents |

## Diagnostics

HTTP failures log transport name, exception chain, request context, timeout/proxy settings, and a stack trace in verbose mode (`RuriLib/Functions/Http/HttpRequestHandler.cs`, `HttpPipelineLogger.cs`). Sensitive headers and cookies are redacted.

Verbose mode is a setting in `GlobalGeneralSettings.VerboseMode` (`RuriLib/Models/Settings/GlobalGeneralSettings.cs`), surfaced through `RuriLib/Providers/General/`.

## Search Shortcuts

```powershell
rg -n "HttpLibrary" RuriLib
rg -n "ExecutePipelineAsync" RuriLib/Functions/Http
rg -n "MultiRunJob" RuriLib Flux.Shared Flux.Native
rg -n "ConfigDebugger" RuriLib
rg -n "OnError" RuriLib Flux.Shared Flux.Native
```
