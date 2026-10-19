# AGENTS.md

Working rules and navigation for AI agents in this repository. Read this before editing code; read [docs/architecture.md](docs/architecture.md) before changing runtime behavior.

## 1. Think Before Coding

Don't assume. Don't hide confusion. Surface tradeoffs.

Before implementing:

- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

Minimum code that solves the problem. Nothing speculative.

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

Touch only what you must. Clean up only your own mess.

When editing existing code:

- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans, remove imports/variables/functions that YOUR changes made unused. Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

Define success criteria. Loop until verified.

Transform tasks into verifiable goals:

- "Add validation" -> "Write tests for invalid inputs, then make them pass"
- "Fix the bug" -> "Write a test that reproduces it, then make it pass"
- "Refactor X" -> "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:

1. [Step] -> verify: [check]
2. [Step] -> verify: [check]
3. [Step] -> verify: [check]

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. Use The Model Only For Judgment Calls

Use for: classification, drafting, summarization, extraction.
Do NOT use for: routing, retries, status-code handling, deterministic transforms.
If code can answer, code answers.

## 6. Surface Conflicts, Don't Average Them

If two patterns contradict, pick one (more recent / more tested). Explain why. Flag the other for cleanup. Don't blend conflicting patterns.

## 7. Read Before You Write

Before adding code, read exports, immediate callers, shared utilities. If unsure why existing code is structured a certain way, ask.

## 8. Tests Verify Intent, Not Just Behavior

Tests must encode WHY behavior matters, not just WHAT it does. A test that can't fail when business logic changes is wrong.

## 9. Checkpoint After Every Significant Step

Summarize what was done, what's verified, what's left. Don't continue from a state you can't describe back. If you lose track, stop and restate.

## 10. Match The Codebase's Conventions, Even If You Disagree

Conformance > taste inside the codebase. If you think a convention is harmful, surface it. Don't fork it silently.

## 11. Fail Loud

"Completed" is wrong if anything was skipped silently. "Tests pass" is wrong if any were skipped. Default to surfacing uncertainty, not hiding it.

---

# Repository Guide

## Scope

- Solution: `Flux.sln`. Every first-party project is in it; there is no web project.
- Runtime: `RuriLib/`, `RuriLib.Http/`, `RuriLib.Proxies/`, `RuriLib.Parallelization/`.
- App: `Flux.Core/` (persistence), `Flux.Shared/` (orchestration), `Flux.Native/` (WPF).
- Vendored: `Libraries/TlsClient.NET/` (nested git repository, locally patched).

If a task concerns runtime logic, jobs, blocks, proxies, or HTTP, start in `RuriLib/` and `Flux.Shared/`. Do not start in `Flux.Native/`.

## Dependency Rules

`RuriLib` → `RuriLib.Http` / `RuriLib.Proxies` / `RuriLib.Parallelization` / `TlsClient.NET`.
`Flux.Core` → `RuriLib`.
`Flux.Shared` → `Flux.Core` + `RuriLib`.
`Flux.Native` → `Flux.Core` + `Flux.Shared`.

- `RuriLib` must never reference `Flux.*`.
- `Flux.Native` is the only composition root (`App.xaml.cs`).
- The desktop UI must not persist jobs or deserialize job options; that belongs to `Flux.Shared/Services/JobOrchestrator.cs`.

## High-Signal Entry Points

### HTTP request block

1. `RuriLib/Blocks/Requests/Http/Methods.cs` (transport switch)
2. `RuriLib/Functions/Http/HttpRequestHandler.cs` (shared pipeline)
3. `HttpClientRequestHandler` / `RLHttpClientRequestHandler` / `TlsClientRequestHandler`
4. `RuriLib/Functions/Http/HttpFactory.cs`
5. `RuriLib.Http/` or `Libraries/TlsClient.NET/` only if the bug is transport-level

`RuriLibHttp` can delegate to the TlsClient path when `UseTlsFingerprinting` is set. Check `RLHttpClientRequestHandler` before assuming a bug is in `RuriLib.Http`.

### Job runtime

1. `RuriLib/Models/Jobs/MultiRunJob.cs`
2. `RuriLib/Models/Jobs/JobInitializer.cs`, `JobLifecycleService.cs`
3. `RuriLib/Models/Jobs/Execution/` (`WorkItemFactory`, `JobResultProcessor`, `JobResourceScope`, `ExecutionHandlerFactory`)
4. `Flux.Shared/Services/JobOrchestrator.cs` and `JobOrchestrator.Projections.cs`
5. `Flux.Core/Services/JobManagerService.cs`, `JobFactoryService.cs`

### Config execution / debugging

1. `RuriLib/Models/Debugger/ConfigDebugger.cs`
2. `RuriLib/Models/Scripting/ScriptPreparationService.cs`
3. `RuriLib/Helpers/Transpilers/` (`Loli2CSharpTranspiler`, `FastLoli2CSharpTranspiler`, `Stack2CSharpTranspiler`)
4. `RuriLib/Helpers/CSharp/ScriptBuilder.cs` (Roslyn + script cache)
5. `RuriLib/Models/Blocks/` then `RuriLib/Blocks/`

### Block registration

- `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs` (explicit built-in list + 4 custom descriptors)
- `RuriLib/Helpers/Blocks/DescriptorsRepository.cs` (attribute reflection, descriptor id = method name)
- `RuriLib/Helpers/Blocks/BlockFactory.cs` (thin lookup only)

Reflection-based discovery is the plugin path (`PluginRepository`), not the built-in path.

## Invariants

These look like cleanup targets and are not. Do not delete or merge them:

- Two LoliCode→C# paths: `FastLoli2CSharpTranspiler` (production) and `Stack2CSharpTranspiler` (step-by-step debugging).
- `Libraries/TlsClient.NET` submodule **and** the `TlsClient.Native.win-x64` NuGet package. The package is the native DLL only.
- All ten icon font packages; they are referenced across XAML.
- `DbRepository<T>` subclasses; the base carries save/attach behavior.
- `HttpLibrary` enum numeric values (`TlsClient = 4`); they are persisted in configs.

More detail and evidence: [docs/architecture.md](docs/architecture.md#deliberate-duplication--do-not-simplify).

## Build And Test

```powershell
dotnet build Flux.sln -c Debug
dotnet test RuriLib.Tests/RuriLib.Tests.csproj
dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj
dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj
```

- SDK is pinned to 8.0.401 by `global.json`.
- Restore writes to the repo-local `packages/` folder (`nuget.config`).
- `RuriLib.Tests` sees `RuriLib` internals via `InternalsVisibleTo`.
- Two HTTPS parity tests are `Skip`ped by design; don't claim full coverage.
- For UI changes, publish with `Flux.Native\test_build\run_testing.bat`; tests cannot verify XAML.

Details: [docs/development.md](docs/development.md).

## Search Tips

```powershell
rg -n "HttpLibrary" RuriLib
rg -n "ExecutePipelineAsync" RuriLib/Functions/Http
rg -n "MultiRunJob" RuriLib Flux.Shared Flux.Native
rg -n "OnError" RuriLib Flux.Shared Flux.Native
rg -n "ConfigDebugger" RuriLib
```

## Change Safety

- Read the whole execution path before refactoring shared runtime code.
- `HttpRequestHandler`, `HttpFactory`, `MultiRunJob`, `JobOrchestrator`, and `ScriptBuilder` have large blast radius.
- If behavior differs between `SystemNet`, `RuriLibHttp`, and `TlsClient`, compare the three handlers before going lower.
- Avoid editing `Libraries/TlsClient.NET/` unless the bug is clearly in that transport; it is a nested repo with a local patch.
- Config and script caches are hash-keyed (script text + `RuriLib` MVID + plugins). A load failure from the cache means a corrupt DLL in `UserData/CompiledScripts/`, not a stale source hash.
- Never renumber serialized enums (`HttpLibrary`, `ConfigMode`, job/proxy enums). Values are persisted in configs, job options, and the SQLite database.

## Non-Code Directories

- `UserData/` — local runtime data (configs, settings, database). Not source.
- `packages/` — NuGet global packages folder for this repo.
- `Changelog/` — per-version notes.
- `Flux.Native/test_build/` — local publish output and backups. Do not treat as source.
