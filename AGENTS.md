# AGENTS.md
1. Think Before Coding
Don't assume. Don't hide confusion. Surface tradeoffs.

Before implementing:

State your assumptions explicitly. If uncertain, ask.
If multiple interpretations exist, present them - don't pick silently.
If a simpler approach exists, say so. Push back when warranted.
If something is unclear, stop. Name what's confusing. Ask.
2. Simplicity First
Minimum code that solves the problem. Nothing speculative.

No features beyond what was asked.
No abstractions for single-use code.
No "flexibility" or "configurability" that wasn't requested.
No error handling for impossible scenarios.
If you write 200 lines and it could be 50, rewrite it.
Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

3. Surgical Changes
Touch only what you must. Clean up only your own mess.

When editing existing code:

Don't "improve" adjacent code, comments, or formatting.
Don't refactor things that aren't broken.
Match existing style, even if you'd do it differently.
If you notice unrelated dead code, mention it - don't delete it.
When your changes create orphans:

Remove imports/variables/functions that YOUR changes made unused.
Don't remove pre-existing dead code unless asked.
The test: Every changed line should trace directly to the user's request.

4. Goal-Driven Execution
Define success criteria. Loop until verified.

Transform tasks into verifiable goals:

"Add validation" → "Write tests for invalid inputs, then make them pass"
"Fix the bug" → "Write a test that reproduces it, then make it pass"
"Refactor X" → "Ensure tests pass before and after"
For multi-step tasks, state a brief plan:

1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.




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
- `RuriLib.Tests/`
  Runtime/debugger integration tests for `RuriLib`, including compiled script and HTTP execution regressions.
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
5. `RuriLib/Models/Jobs/Execution/WorkItemFactory.cs`
6. `RuriLib/Models/Jobs/Execution/JobResultProcessor.cs`
7. `RuriLib/Models/Jobs/Execution/JobResourceScope.cs`
8. `Flux.Shared/Services/JobOrchestrator.cs`
9. `Flux.Shared/Services/JobProjectionService.cs`
10. `Flux.Shared/Services/JobEventSubscriptionService.cs`

### Config execution / debugging flow

1. `RuriLib/Models/Debugger/ConfigDebugger.cs`
2. `RuriLib/Models/Scripting/ScriptPreparationService.cs`
3. `RuriLib/Models/Bots/BotSessionFactory.cs`
4. `RuriLib/Helpers/Transpilers/`
5. `RuriLib/Models/Blocks/`
6. `RuriLib/Blocks/`

Regression coverage for this path belongs in `RuriLib.Tests/`, especially when a bug crosses:
- transpilation
- compiled script cache/load
- debugger execution
- HTTP transport selection

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
- Config stacker state: `Flux.Native/ViewModels/Config/ConfigStackerViewModel.cs`, `ConfigStackerViewModel.Selection.cs`, `ConfigStackerViewModel.Stack.cs`, `ConfigStackerViewModel.Search.cs`, `ConfigStackerViewModel.Tooling.cs`, and `ConfigStackerInspectorViewModel.cs`

## Common Architecture Notes

- `RuriLib` is the execution engine.
- `Flux.Shared` is the orchestration/projection layer around running jobs.
- `Flux.Native` is often UI glue over services from `Flux.Shared` and `RuriLib`.
- `RuriLib.Http` and `RuriLib.Proxies` are lower-level infrastructure, not block-level APIs.
- `TlsClient.NET` is effectively a separate transport stack.

Recent runtime simplifications already in place:
- Job initialization and lifecycle were extracted from `MultiRunJob`.
- `MultiRunJob` now delegates work-item creation, result propagation, hit/stats updates, and runtime-owned cleanup to `WorkItemFactory`, `JobResultProcessor`, and `JobResourceScope`.
- Shared runtime script preparation and bot/session setup now flow through `ScriptPreparationService` and `BotSessionFactory`, used by `JobInitializer`, `WorkItemFactory`, and `ConfigDebugger`.
- Job projection and event subscriptions were extracted from `JobOrchestrator`.
- Desktop job create/edit/clone/delete and job option loading now go through `Flux.Shared/Services/JobOrchestrator.cs`; `Flux.Native` jobs UI should not persist jobs or deserialize job options directly.
- HTTP request execution is now centered on shared pipeline logic in `HttpRequestHandler`.
- Built-in block registration now goes through `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs`; reflection-based descriptor discovery remains for plugin assemblies only.
- `BlockDescriptor` now owns block instance creation through a registered factory, so `BlockFactory` should stay thin and not grow new descriptor-type switches.
- Legacy `HttpCloak` HTTP block settings now normalize to `TlsClient` during load/transpile.
- `HttpRequestBlockInstance` and `ParseBlockInstance` are split into core state, LoliCode parsing/serialization, and C# emission partials.
- Puppeteer browser blocks are now split across `RuriLib/Blocks/Puppeteer/Browser/Methods.cs`, `Methods.Helpers.cs`, `Methods.Cleanup.cs`, `Methods.Launch.cs`, and `Methods.LaunchOptions.cs`.
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
dotnet test RuriLib.Tests/RuriLib.Tests.csproj
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
- `RuriLib/Models/Bots/BotSessionFactory.cs`
- `RuriLib/Models/Debugger/ConfigDebugger.cs`
- `RuriLib/Models/Jobs/MultiRunJob.cs`
- `RuriLib/Models/Jobs/JobInitializer.cs`
- `RuriLib/Models/Jobs/JobLifecycleService.cs`
- `RuriLib/Models/Jobs/Execution/WorkItemFactory.cs`
- `RuriLib/Models/Jobs/Execution/JobResultProcessor.cs`
- `RuriLib/Models/Jobs/Execution/JobResourceScope.cs`
- `RuriLib/Models/Scripting/ScriptPreparationService.cs`
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
