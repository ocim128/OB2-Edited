# Architecture

Flux is a Windows desktop automation platform. `RuriLib` is the execution engine (configs, blocks, jobs, HTTP, browsers); `Flux.Core` owns persistence and domain services; `Flux.Shared` orchestrates jobs and projects them for the UI; `Flux.Native` is the WPF shell. There is no server or web frontend in this repository.

This document describes the paths that matter when changing behavior. For per-screen UI details see [config-menu.md](config-menu.md). For the browser automation stack see [playwright.md](playwright.md).

## Project Map And Dependency Direction

| Project | Target | Role | References |
|---|---|---|---|
| `RuriLib` | `net8.0` | Runtime: blocks, configs, transpilers, scripting, jobs, bots, providers, HTTP dispatch | `RuriLib.Http`, `RuriLib.Proxies`, `RuriLib.Parallelization`, `Libraries/TlsClient.NET` |
| `RuriLib.Http` | `net8.0` | Custom HTTP/1.1 client (`RLHttpClient`) + `ProxyClientHandler` | `RuriLib.Proxies` |
| `RuriLib.Proxies` | `net8.0` | Layer-4 proxied `TcpClient` (HTTP, SOCKS4/4a/5, no-proxy) | none |
| `RuriLib.Parallelization` | `net8.0` | Bots/work-item parallelizer with pause, resume, stop, abort, CPM | none |
| `Flux.Core` | `net8.0` | EF Core SQLite persistence, repositories, config/wordlist/proxy/job services | `RuriLib` |
| `Flux.Shared` | `net8.0` | `JobOrchestrator` (create/start/stop/update jobs), DTO projection, dashboard | `Flux.Core`, `RuriLib`, `RuriLib.Http`, `RuriLib.Proxies`, `RuriLib.Parallelization` |
| `Flux.Native` | `net8.0-windows` (WPF, `win-x64`) | Desktop app, DI composition root, pages/viewmodels | `Flux.Core`, `Flux.Shared` |
| `Libraries/TlsClient.NET` | `netstandard2.1` | Vendored TLS-fingerprint transport (nested git repository, locally patched) | none |

Hard rules that follow from the graph:

- `RuriLib` must not reference any `Flux.*` project. Runtime code cannot depend on the UI or the database.
- `Flux.Native` is the only composition root. Service registration lives in `Flux.Native/App.xaml.cs` (`ConfigureServices`) plus `Flux.Shared/DependencyInjection/ServiceCollectionExtensions.cs` (`AddFluxShared`).
- The desktop UI must not persist jobs or deserialize job options itself. That is `Flux.Shared/Services/JobOrchestrator.cs` and `Flux.Core/Services/JobManagerService.cs` territory.

## Config → Executable Script

A config is `RuriLib/Models/Configs/Config.cs` with four modes (`ConfigMode`): `Stack`, `LoliCode`, `CSharp`, `DLL`. The same config carries all representations: `Stack`, `LoliCodeScript`, `CSharpScript`, `StartupLoliCodeScript`, `StartupCSharpScript`, `DLLBytes`.

Mode conversion (`Config.ChangeMode`) supports only these transitions:

| From | To | Transpiler |
|---|---|---|
| `Stack` | `LoliCode` | `Stack2LoliTranspiler` |
| `Stack` | `CSharp` | `Stack2CSharpTranspiler` |
| `LoliCode` | `Stack` | `Loli2StackTranspiler` |
| `LoliCode` | `CSharp` | `Loli2CSharpTranspiler` |

Every other transition throws. In particular a config cannot be converted back out of `CSharp` or `DLL`.

Preparation is centralized in `RuriLib/Models/Scripting/ScriptPreparationService.cs`, used by `JobInitializer`, `WorkItemFactory`, and `ConfigDebugger`:

1. `TranspileSources` picks the path by mode:
   - `Stack` → `Stack2CSharpTranspiler.Transpile(stack, settings, stepByStep)`
   - `LoliCode` → `Loli2CSharpTranspiler.Transpile(script, settings, stepByStep)`
   - `CSharp` / `DLL` → the stored `CSharpScript` as-is
   - the startup script is always transpiled from `StartupLoliCodeScript`
2. `PrepareAsync` then either loads the DLL (`ConfigMode.DLL` loads `RuriLib.CompiledConfig.Execute` from `DLLBytes`) or compiles the C# through `ScriptBuilder`.

`Loli2CSharpTranspiler` has two implementations, and the difference is behavioral, not cosmetic:

- Default (`stepByStep: false`): `FastLoli2CSharpTranspiler` emits C# directly from LoliCode text without materializing a block stack.
- `stepByStep: true`: LoliCode is parsed into a `Stack` and transpiled with `await data.Stepper.WaitForStepAsync(...)` inserted between blocks. This is what the debugger needs to pause and highlight the current block.

`ScriptBuilder` (`RuriLib/Helpers/CSharp/ScriptBuilder.cs`) compiles with Roslyn and caches:

- in-memory cache keyed by SHA-256 over the generated C# text, optimization level, `RuriLib` assembly identity + module version id, custom `using` directives, and plugin names + module version ids;
- disk cache at `UserData/CompiledScripts/<hash>.dll`, loaded through `CompiledAssemblyScript` when the hash matches.

Because the `RuriLib` module version id is part of the key, rebuilding `RuriLib` invalidates previously cached scripts automatically. Source edits also produce a new hash. A cache entry only goes stale if the DLL on disk is corrupt or partially written — deleting `UserData/CompiledScripts/` is the fix for that, not a normal workflow step.

## Job Execution

Runtime ownership is in `RuriLib`; orchestration and persistence are in `Flux.Shared` / `Flux.Core`.

```
Flux.Native (Jobs UI)
  → IJobOrchestrator (Flux.Shared/Services/JobOrchestrator.cs)
      → JobFactoryService (Flux.Core) builds a Job from JobOptions
      → JobManagerService (Flux.Core) owns live Job instances + persistence
          → MultiRunJob (RuriLib)
              → JobInitializer       prepare config, data pool, proxies, providers
              → JobLifecycleService  start/pause/resume/stop/abort transitions
              → WorkItemFactory      per-work-item BotData + script
              → JobResultProcessor   results, hits, stats
              → JobResourceScope     runtime-owned cleanup
              → ExecutionHandlerFactory → ScriptExecutionHandler | DllExecutionHandler
              → Parallelizer (RuriLib.Parallelization)
```

Notes that are easy to get wrong:

- `MultiRunJob` is still the runtime object that bots run against; it delegates initialization, lifecycle transitions, work-item creation, result propagation, and cleanup to the helpers above. When debugging job behavior, read `MultiRunJob` and the delegated helper together.
- `JobOrchestrator` creates a DI scope per operation (`IServiceScopeFactory`) because the desktop registers repositories as singletons over a scoped `DbContext`. Do not cache a `DbContext` or repository from one scope into another.
- `JobOrchestrator` is split: `JobOrchestrator.cs` holds commands, `JobOrchestrator.Projections.cs` maps runtime state to DTOs.
- Job options are serialized with `TypeNameHandling.Auto` (Newtonsoft). Renaming or moving option types breaks stored jobs.

## HTTP Request Pipeline

Entry point: `RuriLib/Blocks/Requests/Http/Methods.cs`. Transport selection is a switch on `HttpRequestOptions.HttpLibrary`:

| `HttpLibrary` | Handler | Transport |
|---|---|---|
| `RuriLibHttp` (default) | `RLHttpClientRequestHandler` | `RuriLib.Http.RLHttpClient`; delegates to `TlsClientRequestHandler` when `UseTlsFingerprinting` is set |
| `SystemNet` | `HttpClientRequestHandler` | `HttpClient` with a shared, pooled handler |
| `TlsClient` | `TlsClientRequestHandler` | `Libraries/TlsClient.NET` (JA3/JA4 profiles) |

All three handlers inherit `HttpRequestHandler` and share one pipeline (`ExecutePipelineAsync`):

1. `HttpRequestNormalizer.Validate` + `Create*` builds a `NormalizedHttpRequest` (headers, cookies, body, timeout, redirect budget).
2. `HttpPipelineLogger.LogRequest` logs the request with sensitive headers and cookies redacted.
3. A linked `CancellationTokenSource` applies `TimeoutMilliseconds` on top of `BotData.CancellationToken`.
4. The transport sends; `HttpResponseMapper.Apply` writes status, headers, body, and cookies back into `BotData`.
5. `HttpRedirectPolicy` follows redirects manually: bounded by `MaxNumberOfRedirects`, `Authorization` is only replayed to the same host, and HTTPS → HTTP redirects are refused unless `AllowHttpsToHttpRedirect` is enabled.
6. Exceptions are logged with transport name and request context, then rethrown.

`HttpFactory` (`RuriLib/Functions/Http/HttpFactory.cs`) caches `SystemNet` clients by proxy + options, leases them per request, and disposes idle entries after three minutes. `HttpOptions` defaults are connect 5s, read/write 10s, auto-redirect on, 8 redirects.

The `HttpLibrary` enum has numeric gaps (`TlsClient = 4`). Those values were persisted in configs, so do not renumber the enum; the legacy `HttpCloak` value is normalized to `TlsClient` when loading and transpiling (`HttpRequestBlockInstance`).

## Block Model

- Blocks are declared with `[BlockCategory]` on a static class and `[Block]` on static methods (`RuriLib/Attributes`).
- `DescriptorsRepository` reflects over the types listed in `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs` and builds `AutoBlockDescriptor`s. The descriptor **Id is the method name** (`ConstantString`, `HttpRequest`, …); the display name comes from `[Block(name: ...)]` or the readable method name.
- Four custom descriptors are registered explicitly: `KeycheckBlockDescriptor`, `HttpRequestBlockDescriptor`, `ParseBlockDescriptor`, `ScriptBlockDescriptor`. Inline `LoliCodeBlockInstance`s are created by `Loli2StackTranspiler` and are not in the descriptor repository.
- `BlockDescriptor.CreateBlockInstance()` creates instances through the descriptor's registered factory; `BlockFactory.GetBlock<T>(id)` is a thin lookup. Do not add descriptor-type switches back into `BlockFactory`.
- Plugins extend the set at runtime: `RuriLib/Services/PluginRepository.cs` calls `DescriptorsRepository.AddFromExposedMethods(assembly)` for each plugin assembly. That reflection path is plugin-only; built-ins are explicit.
- `BlockInstance` serializes to/from LoliCode (`LABEL:`, parameter lines, `SAFE`, `=> VAR @x`, `ENDBLOCK`). Custom blocks (`HttpRequest`, `Parse`, `Keycheck`, `Script`) have their own `*.LoliCode.cs` and `*.CSharp.cs` partials — change parsing and emission together.

## Persistence And User Data

Everything the desktop app writes lives under `UserData/` next to the executable (`Flux.Native/App.xaml.cs` resolves it from `AppDomain.CurrentDomain.BaseDirectory` and fails startup if it is not writable).

| Path | Contents | Owner |
|---|---|---|
| `UserData/Flux.db` | SQLite database: proxies, proxy groups, wordlists, jobs, records, hits, guests, users | `Flux.Core/ApplicationDbContext.cs` |
| `UserData/Configs/*.opk` | One ZIP archive per config (`readme.md`, `metadata.json`, `settings.json`, `script.*`, optional `build.dll`) | `Flux.Core/Repositories/DiskConfigRepository.cs`, `RuriLib/Helpers/ConfigPacker.cs` |
| `UserData/Environment.ini` | Wordlist types (name, regex, slices), custom statuses, export formats | `RuriLib/Services/RuriLibSettingsService.cs` |
| `UserData/RuriLibSettings.json` | `GlobalSettings`: general, captcha, proxy, Puppeteer, Playwright, Selenium | `RuriLibSettingsService` |
| `UserData/FluxSettings.json` | App-level settings: theme, layout, remote endpoints, `ConfigSectionOnLoad`, auto-save interval | `Flux.Core/Services/FluxSettingsService.cs` |
| `UserData/CompiledScripts/` | Compiled config assemblies used as a warm-start cache | `RuriLib/Helpers/CSharp/ScriptBuilder.cs` |
| `UserData/Plugins/` | Plugin assemblies | `RuriLib/Services/PluginRepository.cs` |
| `UserData/Logs/Jobs`, `UserData/Logs/Crashes` | Job activity logs (when enabled) and crash reports | `RuriLib/Logging/FileJobLogger.cs`, `Flux.Native/App.xaml.cs` |

Schema changes go through EF Core migrations in `Flux.Core/Migrations`. The app applies pending migrations on startup (`Database.MigrateAsync`) in a background task.

Config storage is file-based, not database-backed: `DiskConfigRepository` loads every `*.opk` in `UserData/Configs` and writes changes back to the same file. Deleting the file removes the config.

## Deliberate Duplication — Do Not "Simplify"

These look redundant and are not. Each was checked against the build or a live feature.

- **Two transpiler paths** (`FastLoli2CSharpTranspiler` vs `Stack2CSharpTranspiler`). The fast path is production; the stack path exists for step-by-step debugging. Removing either breaks a real feature.
- **Vendored `TlsClient.NET` plus the `TlsClient.Native.win-x64` NuGet package.** The package ships only the native `tls-client.dll`; the managed wrapper exists only in the submodule, which also carries a local patch. Both are required.
- **Ten icon font packages.** All are referenced from XAML; consolidating means rewriting hundreds of icon references.
- **`DbRepository<T>` subclasses.** The base class carries `SaveChanges`/`Attach` behavior; the subclasses hold entity-specific queries. Replacing them with raw `DbContext` increases coupling.
- **Both `HttpRequestBlockInstance` and `ParseBlockInstance` split into three partials.** Core state, LoliCode parsing/serialization, and C# emission change for different reasons and are easier to review separately.

## Gotchas That Cost Time

- `Flux.Native/Views/Pages/ConfigPages/` uses the namespace `Flux.Native.Views.Pages.Configs`. Folder and namespace do not match.
- The root `appsettings.json` (connection strings, JWT, frontend origins, Serilog) is a leftover from the removed web stack. The desktop app loads `Flux.Native/appsettings.json` from its own output directory; the root file is not read by any project in `Flux.sln`.
- `Directory.Build.props` disables XML docs, suppresses nullable/obsolete warnings, and turns off `GenerateAssemblyInfo`. New public APIs will not produce doc files.
- `RuriLib` exposes internals to `RuriLib.Tests` only (`[assembly: InternalsVisibleTo("RuriLib.Tests")]`). Tests can construct internal handlers; other projects cannot.
- `RuriLib.Parallelization` has its own `ParallelizerType` (`TaskBased`, `ParallelBased`) selected in settings. Both are live; do not delete one without checking `GlobalGeneralSettings.ParallelizerType`.
- `packages/` in the repo root is the NuGet global packages folder (`nuget.config` overrides `globalPackagesFolder`), not a checked-in dependency set.

## Where To Start

| Problem | Read |
|---|---|
| HTTP request fails or behaves differently per transport | `Blocks/Requests/Http/Methods.cs` → `Functions/Http/HttpRequestHandler.cs` → concrete handler → `HttpFactory` → `RuriLib.Http` / `TlsClient.NET` |
| Job will not start, stop, or report progress | `Models/Jobs/MultiRunJob.cs` → `JobInitializer` → `JobLifecycleService` → `Flux.Shared/Services/JobOrchestrator.cs` → `JobManagerService` |
| Block produces wrong C# or wrong LoliCode | block instance under `Models/Blocks/`, implementation under `Blocks/`, glue under `Helpers/Transpilers/` |
| Config fails to compile | `ScriptPreparationService` → `ScriptBuilder` → clear `UserData/CompiledScripts` and retry |
| Desktop page misbehaves | viewmodel under `Flux.Native/ViewModels/` → view under `Flux.Native/Views/` → delegated service |
| Data missing or duplicated in the UI | `Flux.Core/Repositories` → `JobOrchestrator.Projections.cs` → `DashboardService` |
