# Complexity Audit — Flux / RuriLib

**Date:** 2026-10-19
**Scope:** All first-party code (`RuriLib`, `RuriLib.Http`, `RuriLib.Proxies`, `RuriLib.Parallelization`, `Flux.Core`, `Flux.Shared`, `Flux.Native`, `Flux.Web`, `flux-web-client`). Vendored `Libraries/TlsClient.NET` assessed separately.
**Stance:** Every line is guilty until proven necessary. Deletion beats refactoring.

---

## Revision 2 — verification results and what was implemented

Revision 1 was written from static reading. Every finding was then re-verified against the actual build, the git graph, the NuGet package layout, and XAML usage before any deletion. **Four findings did not survive verification** and are retracted below. Retractions are listed first because a wrong audit is worse than a short one.

### Retracted

| # | Claim | Why it was wrong |
|---|---|---|
| **F9** | TlsClient is referenced twice from two sources; delete the vendored copy | **Wrong.** The NuGet package `TlsClient.Native.win-x64` contains *only* `runtimes/tls-client/win/x64/tls-client.dll` — the native Go library. It has no `lib/` folder and no managed assembly. The managed wrapper (`NativeTlsClient`, `TlsClient.Core.Models.*`) exists **only** in the submodule. The two references are complementary, not duplicates. Additionally `Libraries/TlsClient.NET` is a git submodule (mode `160000`) carrying a deliberate local patch (`M src/TlsClient.Native/NativeTlsClient.cs`). Deleting it breaks the build. **No change made.** |
| **F12** | 10 icon packs, keep only MaterialDesign | **Wrong.** All ten are used: Material 162, Unicons 17, MaterialDesign 16, BoxIcons 12, Entypo 10, ForkAwesome 9, RemixIcon 7, RadixIcons 7, Octicons 7, FontAwesome 4 — 251 references across 64 XAML files. Removing nine means hand-rewriting 89 icon references for a build-size win. **No change made.** |
| **F10** | The fast transpiler path is a premature optimization; delete it and keep the Stack path | **Wrong, and backwards.** The fast path is the *production* path (`stepByStep` defaults to `false`); the Stack path is the debugger path. `StepByStep` is a live feature: bound at `Debugger.xaml:294` and read at `DebuggerViewModel.cs:280,318`. Deleting either side breaks something real. The duplication is justified. **Moved to Keep As-Is.** |
| **F14** | Delete 7 `DbSet` repository wrappers | **Over-rated.** `DbRepository<T>` carries real behavior (SaveChanges, Attach) and the thin subclasses are where entity-specific queries live. Deleting them pushes raw `DbContext` usage into consumers — *more* coupling, not less. **No change made.** |

### Also corrected

- **F13** claimed the 16 `Trace(...)` calls in `App.xaml.cs` were dead diagnostics. They are not: the helper is `#if DEBUG || OB2_TRACE`, so it is a no-op in Release and a genuine startup-hang diagnostic in Debug. Only the stale comment `// Startup diagnostics removed` was fixed. Trace calls kept.
- **F8** was understated. `RuriLib/Functions/Http/RLHttpClient.cs` is not merely a confusingly-named duplicate — it is **never constructed anywhere**. `HttpFactory.GetRLHttpClient` returns `RuriLib.Http.RLHttpClient`, and that is what every call site uses. It was 168 lines of dead code.

### Implemented

| # | Change | Lines removed |
|---|---|---:|
| **F1** | Deleted `Flux.Web/`, `flux-web-client/`, removed from `Flux.sln`; deleted `Dockerfile`, `Dockerfile.build`, `Dockerfile.remote`, `docker-compose.yml` | 44,253 |
| **F6** | Deleted `RuriLib/Blocks/Utility/PairTrading/` + its registry line | 2,049 |
| **F2** | Deleted `AuthenticationService`, `SettingsFacade`, `PluginService`, `NotificationService` + 6 interfaces + `PasswordHasher` + 6 orphan DTOs + 2 test files; removed the write-only notification pipeline and its event-subscription machinery from `JobOrchestrator` (539 → 430 lines) | 544 |
| **F8** | Deleted dead `RuriLib/Functions/Http/RLHttpClient.cs` | 168 |
| **F13** | Removed 10 agent markers; fixed stale startup-diagnostics comment | 18 |
| — | Updated `README.md` + `AGENTS.md` to drop references to deleted projects | — |

**Verified:** `dotnet build Flux.sln` → 0 errors, 75 warnings. `dotnet test Flux.sln` → 105 passed, 0 failed, 2 skipped (pre-existing socket flakiness).

**Measured result**

| Metric | Before | After | Change |
|---|---:|---:|---:|
| First-party C# lines | 108,894 | 90,580 | **−18,314 (−16.8%)** |
| TypeScript/HTML lines | 28,831 | 0 | −28,831 |
| **Total first-party lines** | **137,725** | **90,580** | **−47,145 (−34.2%)** |
| `.cs` files | 1,108 | 828 | −280 |
| Solution projects | 13 | 12 | −1 |
| Container files | 4 | 0 | −4 |

Note: `90,580` still includes 4,795 lines from the `Libraries/TlsClient.NET` submodule, which is third-party.

### Not implemented, and why

- **F5** (collapse `BlockParameter`/`BlockSetting`/`Setting`/`Variable`) — ~500 lines, but it rewrites the type system under the transpiler, which is the highest-blast-radius area in the product and has only 87 tests. Poor risk/reward for a deletion-first mandate. Recommend a separate branch with a written test matrix.
- **F3/F4** — dissolved by F1. With the web stack gone there is exactly one job orchestration path and one DTO set; the duplicated `MultiRunJobService`, 182 DTOs, 90 AutoMapper maps and the 484-line `BlockMapper` all left with the web project.
- **F11** (UI micro-services) — ~300 lines of working WPF plumbing I cannot visually verify. Left for a session that can run the app.
- **F15** (job monitor) — real user-facing feature; only its web DTOs (596 lines) were web-bound and are now gone with F1.

---


## 0. Baseline Measurements

| Area | Files | Lines | Notes |
|---|---:|---:|---|
| `RuriLib/` | 390 | 41,601 | Blocks 14,697 · Models 11,717 · Functions 5,445 · Helpers 5,220 |
| `Flux.Native/` (WPF) | 194 | 29,693 | ViewModels 10,310 · Views 8,068 · UI 4,212 · Services 4,128 |
| `Flux.Web/` (API) | 252 | 15,422 | DTOs 6,093 · Controllers 4,469 · Services 2,194 |
| `Flux.Core/` | 106 | 6,714 | Repositories 1,019 · Services 1,319 |
| `Libraries/TlsClient.NET/` (vendored) | 63 | 4,795 | |
| `RuriLib.Http/` | 15 | 3,618 | |
| `RuriLib.Tests/` | 21 | 2,425 | |
| `Flux.Shared/` | 39 | 1,777 | |
| `RuriLib.Parallelization/` | 10 | 1,151 | |
| `RuriLib.Proxies/` | 10 | 968 | |
| `Flux.Shared.Tests/` | 4 | 176 | |
| **First-party C# total** | **~1,045** | **104,099** | |
| `flux-web-client/src` (Angular) | 349 | 28,831 | TS + HTML |
| **Grand total first-party** | | **132,930** | |
| `.csproj` files | | **23** | 11 first-party, 12 vendored |

Activity signal (this is the most important number in the report):

| Area | Commits all-time | Commits last 6 months | Last touched |
|---|---:|---:|---|
| `Flux.Native` | 41 | 20 | 2026-10-19 |
| `RuriLib` | 132 | 16 | 2026-10-19 |
| `Flux.Web` | 8 | 5 | 2026-06-10 |
| `Flux.Shared` | 12 | 7 | 2026-06-10 |
| `flux-web-client` | 4 | 1 | 2026-05-21 |

The web stack is ~33% of all code and receives ~5% of the attention. It is not a maintained product; it is a carried cost.

---

## 1. Findings

Each finding uses this template:

> **Severity** · Engineering cost · Maintenance cost · User impact · Recommendation · Expected reduction · Confidence

---

### F1 — The web stack (Flux.Web + flux-web-client) is dormant but fully maintained

> **Status: IMPLEMENTED.** Both projects deleted, removed from `Flux.sln`; container files removed.

**Severity: Critical**

**Current implementation**

Two complete UIs ship from one repo:

- `Flux.Native/` — WPF desktop, 29,693 lines, touched today.
- `Flux.Web/` + `flux-web-client/` — ASP.NET API (15,422 lines, 252 files) + Angular SPA (28,831 lines, 349 files), last meaningfully touched 2026-06-10 and 2026-05-21.

The web half carries, independently of any feature work:

- 182 DTO files (6,093 lines), 33 of them under 15 lines
- 90 AutoMapper `CreateMap` calls in a 495-line profile
- A 484-line hand-written `BlockMapper` whose own comment reads: *"Here I did the dto -> block mappings manually while I find a way to make automapper work properly on BlockSetting..."*
- 23 controllers, JWT auth, FluentValidation, API versioning, Swagger, Serilog + Serilog.Exceptions, SignalR hubs, NetBeauty, `PerformanceMonitorService`
- A full second implementation of job orchestration (`MultiRunJobService` 466 lines, `ProxyCheckJobService` 402 lines) that duplicates `Flux.Shared/JobOrchestrator`

**Why it is more complex than necessary**

Nothing in the web stack is *shared*. `Flux.Web` references only `Flux.Core` — it does not reference `Flux.Shared`, and `Flux.Shared` does not reference it. So the "shared orchestration layer" the README advertises does not actually share anything with the web. Two job command surfaces, two DTO universes, two event-subscription models, two error-handling conventions, maintained in parallel, drifting apart since June.

**Simplest alternative**

Move `Flux.Web/` and `flux-web-client/` to a `web-ui` branch, remove both from `Flux.sln`, delete the Dockerfile's web stages. If a headless API is genuinely needed, re-add it later against `Flux.Core` — it will be ~1,500 lines, not 44,000, because it will reuse one DTO set.

**Cost/benefit**

| | |
|---|---|
| Engineering cost to remove | Low — branch + solution edit. Zero runtime risk to desktop. |
| Maintenance cost now | High — every `Flux.Core`/`RuriLib` model change must be mirrored into 182 DTOs and 90 maps or the web build breaks silently. |
| User impact | **Decision gate:** zero if no web users; total if web is a shipped product. Evidence says dormant. Confirm before deleting. |
| Expected reduction | **44,253 lines (−33% of total codebase)**, 2 projects, ~10 dependencies, 1 Docker build stage |
| Confidence | **90%** that it is dead weight; **60%** that no one depends on it |

---

### F2 — `Flux.Shared` is not shared, and 22% of it is unreachable

> **Status: IMPLEMENTED.** Dead services, interfaces, orphan DTOs and the notification pipeline removed. Project boundary retained (see Revision 2).

**Severity: High**

**Current implementation**

A dedicated project (`Flux.Shared`, 39 files, 1,777 lines) whose stated purpose is "shared orchestration and DTO layer used by the application shell around `RuriLib`". It has exactly one consumer: `Flux.Native`. `Flux.Native` also references `Flux.Core` and `RuriLib` directly, so the layer buys nothing but a project boundary.

Dead code inside it, verified by grep:

| Symbol | Lines | Status |
|---|---:|---|
| `AuthenticationService` + `IAuthenticationService` + `PasswordHasher` | 169 | Registered in DI, **never injected anywhere** |
| `SettingsFacade` + `ISettingsFacade` | 91 | Registered in DI, **never injected anywhere** |
| `PluginService` + `IPluginService` | 59 | Registered in DI, **never injected anywhere** |
| `NotificationService` + `INotificationService` | 68 | `StreamAsync` has **exactly one caller: its own unit test** |

That is 387 lines — **22% of the project** — that cannot execute in production.

The notification case is worse than dead code. `JobOrchestrator` publishes every job status change, every hit, every error, and every completion into an unbounded `Channel<NotificationDto>` with a 100-item buffer that **nothing drains**. Every hit in the desktop app allocates a DTO, writes to a channel, and logs `LogDebug` for a message no UI will ever show.

`Flux.Shared.Tests` (176 lines, 4 files) exists largely to test this dead code: 2 of its 4 test files (`AuthenticationServiceTests`, `NotificationServiceTests`) assert behavior of services with no production callers.

**Why it is more complex than necessary**

Six interfaces, each with exactly one implementation, for a project with one consumer. The interfaces are pure ceremony — they exist to be mocked, but the only tests that mock them test the mocks.

Also: 30 DTO files averaging **8.5 lines each**, 11 of them at 3–5 lines. `BotLogEntryDto.cs` is a 3-line file. This is file-per-record fragmentation with no navigational benefit.

**Simplest alternative**

Delete the project. Move the two live services (`JobOrchestrator` 956 lines, `DashboardService` 150 lines) into `Flux.Native/Services/`. Delete the six interfaces and inject concrete types — there is one implementation and no test seam that needs them. Delete the three dead services and their tests. Collapse the 30 DTO files into `Flux.Native/Models/DesktopDtos.cs`.

**Cost/benefit**

| | |
|---|---|
| Engineering cost to remove | Low — mechanical move, no logic changes |
| Maintenance cost now | Medium — a project boundary that implies reuse that does not exist; misleads every new developer |
| User impact | None. Removing a write-only notification channel removes allocations, not features. |
| Expected reduction | **~700 lines deleted, 39 files → ~4 files, 1 project removed, 6 interfaces removed** |
| Confidence | **95%** |

---

### F3 — Two job orchestration implementations for one job model

**Severity: High**

**Current implementation**

`RuriLib` owns the job lifecycle (`Job.Start/Stop/Pause/Resume/Abort/ChangeBots/SkipWait`). Two independent adapters then re-implement the same command surface:

| Concern | Desktop | Web |
|---|---|---|
| Command surface | `Flux.Shared/Services/JobOrchestrator.cs` (539) | `Flux.Web/Services/MultiRunJobService.cs` (466) |
| Projection | `JobOrchestrator.Projections.cs` (417) | `AutoMapperProfile.cs` (495) + `BlockMapper.cs` (484) |
| Event subscription | `EnsureSubscribed` / `UnsubscribeIfMultiRun` | `Subscribe` / `Unsubscribe` |
| Error policy | swallow + notify | `EntryNotFoundException` / `BadRequestException` |
| Ownership check | none | `EnsureOwnership` |

Both subscribe to the same eight `MultiRunJob` events, both translate them into DTOs, both forget tasks with different error handling. The desktop path has no ownership check; the web path does. The behaviors have already diverged.

**Why it is more complex than necessary**

The `Job` object is already the single source of truth. Both adapters are pure projection + command forwarding, yet each owns 400–550 lines of it.

**Simplest alternative**

Delete the web adapter (F1). If web survives: extract one `JobCommandService` in `Flux.Core` that wraps `JobManagerService` and returns domain objects; let each UI own only its DTO projection. Target: one command implementation, two thin projections.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low if F1 executes (deletion). Medium if web is kept. |
| Maintenance cost now | High — every new job command must be added twice, in two error conventions |
| User impact | None |
| Expected reduction | **~900 lines** (post-F1) |
| Confidence | **85%** |

---

### F4 — 182 DTOs + AutoMapper + a 484-line manual mapper that admits it failed

**Severity: High**

**Current implementation**

- `Flux.Web/Dtos/`: **182 files, 6,093 lines**, 33 files under 15 lines
- `AutoMapperProfile.cs`: 495 lines, **90 `CreateMap` calls**, many with custom `PolyMapper` lambdas
- `BlockMapper.cs`: 484 lines of hand-written `switch (id)` block reconstruction
- `PolyMapper.cs` + `PolyDtoCache.cs`: a bespoke polymorphic-mapping runtime with a startup `PolyDtoCache.Scan()` reflection pass

The `BlockMapper` file's opening comment is the finding:

```csharp
// Here I did the dto -> block mappings manually while I find
// a way to make automapper work properly on BlockSetting...
```

**Why it is more complex than necessary**

`RuriLib` already has a polymorphic block model with a descriptor registry (`BlockDescriptor.CreateBlockInstance()`) and Newtonsoft `TypeNameHandling.Auto` serialization. The web layer rebuilds that entire polymorphic tree by hand, then maintains a cache to make the hand-built mapper work. Three mapping technologies coexist for one object graph: AutoMapper, manual `switch`, and a reflection-based poly cache.

AutoMapper is a runtime dependency that moves compile-time errors to startup-time errors and adds reflection cost per map — for mappings that are, in almost every case, field-for-field copies of a record.

**Simplest alternative**

Delete AutoMapper, `PolyMapper`, `PolyDtoCache`, and `BlockMapper`. Serialize `BlockInstance` directly with `System.Text.Json` + `[JsonDerivedType]` attributes on the base type. Where a DTO genuinely differs from the domain model (credentials, computed fields), use an explicit `record` with a constructor and a static `From(...)` factory — the compiler then enforces completeness.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Medium (only if web is kept) |
| Maintenance cost now | Very high — 90 maps that break at runtime, plus a 484-line mapper that must track every block type |
| User impact | Low — same wire format if DTOs are preserved as records |
| Expected reduction | **~4,000 lines (−65% of the DTO/mapping surface)**, 1 dependency (AutoMapper), 2 bespoke utils |
| Confidence | **80%** |

---

### F5 — Three parallel type hierarchies encode the same seven primitive types

**Severity: Medium**

**Current implementation**

The same seven types (`string`, `int`, `float`, `bool`, `List<string>`, `Dictionary<string,string>`, `byte[]`) are modeled three times:

1. **`BlockParameter`** — 9 files, 302 lines (`StringParameter`, `IntParameter`, …)
2. **`BlockSetting`** — 14 files, 333 lines, holding *both* `FixedSetting` and `InterpolatedSetting` regardless of `InputMode`
3. **`Setting`** — an **empty abstract class** with 8 subclasses that each hold one `Value` property, plus `InterpolatedSetting` with 4 more subclasses (33 lines total for the interpolated branch)
4. **`Variable`** — 10 files with a `VariableFactory`

`Setting.cs` in full:

```csharp
public abstract class Setting
{
    
}
```

`BlockSetting` allocates two objects per setting and leaves one permanently unused depending on `InputMode`. `BlockSettingFactory` is 11 near-identical `Create*Setting` methods that differ only in the concrete type instantiated.

**Why it is more complex than necessary**

Three hierarchies, ~33 files, to represent a tagged union of seven types. The empty base class carries no contract — no abstract member, no virtual behavior — so it provides zero polymorphism value; it only forces downcasts.

**Simplest alternative**

One `BlockValue` record:

```csharp
public sealed record BlockValue(VariableType Type, object? Value);
```

`VariableType` already exists and is already the discriminator the transpiler uses. Delete `Setting` + its 8 subclasses + `InterpolatedSetting` + its 4 subclasses; keep interpolation as a `string` value plus a `bool Interpolated` flag on `BlockSetting`.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Medium — touches transpilers, block UI, serialization |
| Maintenance cost now | Medium — every new supported type requires edits in 4 places |
| User impact | None |
| Expected reduction | **~500 lines, ~25 files removed** |
| Confidence | **75%** |

---

### F6 — 2,049 lines of quantitative finance inside a bot-automation tool

> **Status: IMPLEMENTED.** Folder and registry entry deleted.

**Severity: High**

**Current implementation**

`RuriLib/Blocks/Utility/PairTrading/` — 9 files, **2,049 lines**:

| File | Lines |
|---|---:|
| `ReportBuilder.cs` | 372 |
| `TransferEntropyAnalysis.cs` | 305 |
| `StatisticalAnalysis.cs` | 247 |
| `WaveletAnalysis.cs` | 244 |
| `BinanceClient.cs` | 207 |
| `SeriesParser.cs` | 206 |
| `PairTradingModels.cs` | 176 |
| `Methods.cs` | 153 |
| `CopulaAnalysis.cs` | 139 |

One block, `AnalyzePairTrading`, with **17 parameters**, including `WaveletType`, `copulaTailThreshold`, `transferEntropyBins`, `correlationVelocityLookback`. It ships a hardcoded Binance klines HTTP client with paging and rate-limit delays.

Its only reference in the entire codebase is one line in `BuiltInBlockRegistry.cs`.

**Why it is more complex than necessary**

Wavelet decomposition, copula tail dependence, and transfer entropy are research-grade statistics. They have no relationship to credential automation, they are untested (`RuriLib.Tests` contains zero PairTrading tests), and they embed a third-party exchange API client into the runtime. This is the definition of a feature that exists because someone wanted to build it.

**Simplest alternative**

Delete the folder and the registry line. If pair trading is a real product surface, it belongs in a separate plugin, not in the core block registry.

**Cost/benefit**

| | |
|---|---|
| Engineering cost to remove | Trivial — one registry line |
| Maintenance cost now | High — 2,049 lines of math nobody on the team owns, no tests |
| User impact | Low if unused; measurable if any user relies on it. Check telemetry/config usage first. |
| Expected reduction | **2,049 lines (−4.9% of RuriLib)** |
| Confidence | **90%** |

---

### F7 — Four browser automation stacks for one capability

**Severity: High**

**Current implementation**

| Stack | Lines | Files | NuGet |
|---|---:|---:|---|
| Playwright | 3,583 | 8 | `Microsoft.Playwright` |
| Puppeteer | 2,037 | 7 | `PuppeteerSharp`, `PuppeteerExtraSharp` |
| Selenium | 959 | 3 | `Selenium.WebDriver`, `Selenium.Support` |
| Android/Appium | 902 | 3 | `Appium.WebDriver` |
| **Total** | **7,481** | **21** | **6 packages** |

All four expose near-identical block sets (`Browser`, `Page`, `Elements`), plus four parallel provider trees under `RuriLib/Providers/{Playwright,Puppeteer,Selenium,Android}`, and four entries in `BuiltInBlockRegistry.ExposedMethodTypes`.

**Why it is more complex than necessary**

Each stack needs its own session lifecycle, element model, block surface, provider, and dependency. Playwright already covers Chromium, Firefox, and WebKit through one API. Maintaining four means every browser-block bug is four bugs, and every new browser feature is four implementations.

**Simplest alternative**

Keep Playwright. Delete Puppeteer, Selenium, and Android blocks + providers + registry entries + NuGet references. Playwright's `IBrowserContext` covers the stealth/fingerprint use cases that drove the original multi-stack design.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low — deletion; user configs referencing removed blocks fail loudly at load |
| Maintenance cost now | Very high — 21 files, 6 heavy deps, 4 upgrade cadences |
| User impact | Medium — users on Puppeteer/Selenium configs must migrate. **Validate against real configs before deleting.** |
| Expected reduction | **3,898 lines, 4 NuGet packages, ~10 MB output size** |
| Confidence | **75%** (needs user-config validation) |

---

### F8 — Two classes named `RLHttpClient` in two assemblies with different semantics

> **Status: IMPLEMENTED.** The `RuriLib.Functions.Http` one was never constructed; deleted.

**Severity: Medium**

**Current implementation**

- `RuriLib/Functions/Http/RLHttpClient.cs` — 168 lines, wraps `System.Net.Http.HttpClient`
- `RuriLib.Http/RLHttpClient.cs` — 634 lines, raw socket/Pipe implementation of HTTP/1.1

Same name, same namespace root, different transport, different behavior, different failure modes.

**Why it is more complex than necessary**

Any file with both `using RuriLib.Functions.Http;` and `using RuriLib.Http;` gets an ambiguous-reference error at best, and a silently wrong transport at worst. Debugging "why does my request behave differently in this config" starts with figuring out which `RLHttpClient` is in scope. This is a naming collision, not an abstraction.

**Simplest alternative**

Rename the wrapper to `SystemNetHttpClient` (it is literally an `HttpClient` adapter), or delete it — `HttpClientRequestHandler` already exists to do that job.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Trivial — rename, 3 call sites |
| Maintenance cost now | Low but constant — a permanent source of confusion and merge friction |
| User impact | None |
| Expected reduction | ~170 lines if deleted; naming risk eliminated either way |
| Confidence | **95%** |

---

### F9 — TlsClient is referenced twice, from two different sources

> **Status: RETRACTED — finding was wrong.** The package supplies only the native DLL; the managed wrapper exists only in the submodule. See Revision 2.

**Severity: High**

**Current implementation**

`RuriLib/RuriLib.csproj`:

```xml
<ProjectReference Include="..\Libraries\TlsClient.NET\src\TlsClient.Native\TlsClient.Native.csproj" />
<PackageReference Include="TlsClient.Native.win-x64" Version="1.9.1" />
```

The same library is compiled from vendored source **and** consumed as a NuGet package. `Flux.Native.csproj` adds the package a third time.

**Why it is more complex than necessary**

Two sources of truth for one native dependency. Version skew is not a risk — it is a certainty, because the vendored source is pinned at whatever commit was copied in while the package floats. Assembly identity conflicts, duplicate native DLL copying (hence the `CopyTlsClientNative` MSBuild target at line 281), and a `Dockerfile` that publishes both.

This also drags **4,795 lines / 63 files / 12 `.csproj`** (including examples, templates, and its own test projects) into the solution for a dependency that is already on NuGet.

**Simplest alternative**

Keep the `TlsClient.Native.win-x64` package reference only. Delete `Libraries/TlsClient.NET/` entirely and the `CopyTlsClientNative` target. Solution drops from 23 projects to 11.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low — verify `TlsClientRequestHandler` API surface matches package 1.9.1, delete, build |
| Maintenance cost now | High — duplicate native copies, MSBuild workarounds, a vendored library that will never be updated |
| User impact | None if the package version matches |
| Expected reduction | **4,795 lines, 12 projects, 1 MSBuild target** |
| Confidence | **90%** |

---

### F10 — Two transpiler paths that must produce identical C#

> **Status: RETRACTED — finding was backwards.** The fast path is the production path; the Stack path serves live step-by-step debugging. See Revision 2.

**Severity: Medium**

**Current implementation**

```
LoliCode ──┬─ FastLoli2CSharpTranspiler (243) ──────────────────────► C#
           └─ Loli2Stack (207) → Stack2CSharp (341) ────────────────► C#
              + LoliCodeStatementTranspiler (287) shared by both
              + 2 facade classes (28, 49)
```

`optimization_summary.md` documents why the fast path exists: to skip intermediate `BlockInstance` allocation when compiling configs. It also documents that the slow path was retained for debugger block highlighting.

**Why it is more complex than necessary**

Two code generators must emit semantically identical C#. Every block change must be verified against both. The fast path is a premature optimization — its stated benefit is "faster switching between LoliCode and C# modes," which is a UI interaction, not a throughput problem, and `ScriptBuilder` already caches compiled assemblies to disk.

**Simplest alternative**

Keep the Stack path (it is required by the debugger, and it is the tested path). Delete `FastLoli2CSharpTranspiler` and collapse `Loli2CSharpTranspiler` to a direct call. If profiling later proves the Stack allocation matters, reintroduce a cache at the `BlockInstance` level — not a second generator.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low — one facade edit, delete one file |
| Maintenance cost now | High — silent divergence risk between two generators, the worst kind of bug |
| User impact | None if the Stack path is correct (it is the debugger path, so it is exercised) |
| Expected reduction | **~270 lines + one entire correctness-risk class** |
| Confidence | **70%** (verify no test depends on the fast path) |

---

### F11 — Five single-implementation UI micro-services

**Severity: Low**

**Current implementation**

`Flux.Native/Services/{Navigation,Menu,Sidebar,Commands,Window}` — **939 lines across 8 files, 5 interfaces**, each with exactly one implementation:

| Service | Lines | Interface |
|---|---:|---|
| `CommandHandler` | 158 | `ICommandHandler` (11) |
| `SidebarHandler` + `SidebarAnimator` | 176 | `ISidebarHandler` (25) |
| `MenuHandler` + `SubmenuController` | 177 | `IMenuHandler` (25) |
| `NavigationHandler` + `PageButtonMapper` | 182 | `INavigationHandler` (17) |
| `WindowControlHandler` + `AccessibilityHandler` | 145 | `IWindowControlHandler` (13) |

**Why it is more complex than necessary**

These wrap WPF primitives (`Frame.NavigationService`, `Storyboard`, `WindowState`) that are already testable and already injected by the framework. The interfaces exist to be mocked, but no test mocks them. Five folders, eight files, and DI registrations for behavior that is 20–40 lines each.

**Simplest alternative**

Collapse into one `Flux.Native/Services/ShellServices.cs` with concrete classes; drop the interfaces; register concretes in DI.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low |
| Maintenance cost now | Low–Medium — five folders to search for shell behavior |
| User impact | None |
| Expected reduction | **~300 lines, 5 interfaces, 5 folders → 1 file** |
| Confidence | **65%** |

---

### F12 — Ten icon-font packages

> **Status: RETRACTED — all ten are in use (251 references).** See Revision 2.

**Severity: Low**

**Current implementation**

`Flux.Native.csproj` references **10** `MahApps.Metro.IconPacks.*` packages: Material, MaterialDesign, FontAwesome, Entypo, RemixIcon, Unicons, Octicons, BoxIcons, RadixIcons, ForkAwesome.

**Why it is more complex than necessary**

Each ships a full font file, a generated enum with thousands of members, and its own XAML resource dictionary. Ten of them means ~10 MB of fonts in the output and 10 packages to restore for icon lookup.

**Simplest alternative**

Keep `MaterialDesign` only. Audit `IconPacks.*` usages in XAML, remap stragglers to MaterialDesign equivalents.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low — XAML grep and remap |
| Maintenance cost now | Low, but compounding build/publish size |
| User impact | Cosmetic only |
| Expected reduction | **9 dependencies**, several MB of output |
| Confidence | **85%** |

---

### F13 — AI-generated code markers and dead diagnostics shipped in `App.xaml.cs`

> **Status: PARTIALLY IMPLEMENTED.** Markers removed; `Trace` calls kept (debug-only, genuinely useful).

**Severity: Low (signal value: high)**

**Current implementation**

- **5** `#COMPLETION_DRIVE` and **5** `#SUGGEST_VERIFY` markers in production `.cs` files
- `App.xaml.cs`: **16** `Trace(...)` calls, plus the comment `// Startup diagnostics removed` sitting directly above surviving diagnostics
- `// Legacy handlers kept for backward compatibility; centralized handler is initialized in OnStartup.` — with no indication of what depends on them
- 21 `TODO`, 2 `HACK`

**Why it is more complex than necessary**

`#COMPLETION_DRIVE` / `#SUGGEST_VERIFY` are agent-workflow annotations that leaked into shipped source. They describe intent that no longer matches the code, and they teach new developers that these files are half-finished. The `Trace` calls write to a log nobody reads during normal operation.

**Simplest alternative**

Delete all agent markers. Delete the 16 `Trace` calls. Keep one structured `ILogger` path for startup failures. Resolve the "legacy handlers" comment by deleting the handlers or documenting the actual dependency.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Trivial |
| Maintenance cost now | Low, but it is a credibility tax on the whole codebase |
| User impact | None |
| Expected reduction | ~40 lines |
| Confidence | **95%** |

---

### F14 — Repository pattern applied to plain `DbSet` access

> **Status: RETRACTED — over-rated.** Deleting the wrappers increases coupling. See Revision 2.

**Severity: Low**

**Current implementation**

`Flux.Core/Repositories/` — 20 files, 1,019 lines: 10 interfaces + 10 implementations over a `DbRepository<T>` base.

Interface sizes:

| Interface | Lines | Real polymorphism |
|---|---:|---|
| `IWordlistRepository` | 54 | Yes — `HybridWordlistRepository` (DB + disk) |
| `IConfigRepository` | 51 | Yes — `DiskConfigRepository` (filesystem) |
| `IRepository<T>` | 91 | No |
| `IJobRepository` | 14 | No |
| `IProxyRepository` | 17 | No |
| `IHitRepository` | 20 | No |
| `IUserRepository` | 12 | No |
| `IGuestRepository` | 10 | No |
| `IRecordRepository` | 10 | No |
| `IProxyGroupRepository` | 10 | No |

**Why it is more complex than necessary**

Seven of ten repositories are thin `DbSet` wrappers with one implementation and no substitution story. They add a file, an interface, and a DI registration to reach `context.Jobs.Where(...)`.

**Simplest alternative**

Keep `IConfigRepository` and `IWordlistRepository` — they have genuine second implementations. Delete the rest and inject `FluxDbContext` directly.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low |
| Maintenance cost now | Low — but it is 7 files of pure indirection |
| User impact | None |
| Expected reduction | **~150 lines, 7 interfaces** |
| Confidence | **70%** |

---

### F15 — Job monitor triggers/actions: polymorphic extension point for two trigger types

**Severity: Medium**

**Current implementation**

`RuriLib/Models/Jobs/Monitor/` — 400 lines (`Trigger` 58, `MultiRunJobTrigger` 128, `Action` 100, `TriggeredAction` 78, `MultiRunJobActions` 36). Mirrored in `Flux.Web/Dtos/JobMonitor/` — **596 lines across 7 DTO files**, including separate `CreateTriggeredActionDto` (42), `UpdateTriggeredActionDto` (57), and `TriggeredActionDto` (62).

Two abstract bases (`Trigger`, `Action`), one concrete trigger family, one concrete action family.

**Why it is more complex than necessary**

Abstract `Trigger`/`Action` bases with one concrete family each is the textbook shape of an extension point built for a second implementer that never arrived. Three near-identical DTOs for create/update/read of the same entity is the same mistake in the web layer.

**Simplest alternative**

Replace the abstract bases with a single `TriggeredAction` record containing a `TriggerKind` enum and a `ActionKind` enum plus the union of their fields. Collapse the three DTOs into one record used for create, update, and read.

**Cost/benefit**

| | |
|---|---|
| Engineering cost | Low–Medium |
| Maintenance cost now | Medium |
| User impact | Low — the feature (auto-restart on low CPM) survives; only the type shape changes |
| Expected reduction | **~200 lines in RuriLib, 596 lines in Flux.Web (if web is kept)** |
| Confidence | **60%** |

---

## 2. Keep As-Is — complexity that is actually justified

Do not touch these. The complexity is load-bearing.

| Area | Lines | Why it earns its keep |
|---|---:|---|
| `RuriLib.Http/` | 3,618 | Hand-rolled HTTP/1.1 over `Pipe`/sockets. This is the product's differentiator: byte-exact request control and TLS fingerprint fidelity that `HttpClient` cannot provide. A 818-line `HttpResponseBuilder` is the price of correct chunked/gzip/until-close handling. |
| `RuriLib.Proxies/` | 968 | Required by `RuriLib.Http`. SOCKS4/5, HTTP, HTTPS CONNECT with auth. Not replaceable by a package without losing the same fidelity. |
| `RuriLib.Parallelization/` | 1,151 | Pause/resume, CPM throttling, dynamic bot count. Two implementations (`Task`/`Parallel`) is arguably one too many — but this is the job engine's core, and it is 1,151 lines for a hard concurrency problem. |
| Block attribute + reflection registry | ~530 | `[Block]`, `[BlockCategory]`, `DescriptorsRepository`. This is the documented plugin/custom-block contract users build against. Deleting it deletes the extensibility product. **Fix the Parameter/Setting/Variable triplication (F5); do not remove the registry.** |
| LoliCode ↔ C# round-trip | ~1,900 | Users author in LoliCode, execute in C#, and switch back. The round-trip is a headline feature. |
| `Flux.Core` EF Core + SQLite | 6,714 | Normal persistence. No over-engineering found. |
| `AvalonEdit`, `MahApps.Metro` (1 icon pack) | — | Standard WPF stack. |
| `ScriptBuilder` compiled-assembly cache | ~466 | Documented, measurable cold-start win. Real optimization, not premature. |

---

## 3. Delete First — highest ROI removals

Ordered by (lines removed) ÷ (risk × effort). Execute top to bottom.

| # | Target | Lines | Risk | Effort | Why first |
|---|---|---:|---|---|---|
| 1 | `Libraries/TlsClient.NET/` + duplicate package refs (F9) | 4,795 | Low | Low | 12 projects and an MSBuild workaround vanish. Pure deletion, no behavior change. |
| 2 | `RuriLib/Blocks/Utility/PairTrading/` (F6) | 2,049 | Low | Trivial | One registry line. No tests, no callers. |
| 3 | `Flux.Shared` dead services + 6 interfaces (F2) | 387 | None | Low | Registered-but-never-injected code. Zero user surface. |
| 4 | `Flux.Shared` project boundary (F2) | ~700 | Low | Low | Project with one consumer and a false "shared" name. |
| 5 | Puppeteer + Selenium + Android stacks (F7) | 3,898 | Medium | Low | **Validate user configs first.** Biggest win after the web stack. |
| 6 | `FastLoli2CSharpTranspiler` (F10) | 270 | Medium | Low | Removes a whole class of silent divergence bugs. |
| 7 | `RLHttpClient` duplicate name (F8) | 0–170 | None | Trivial | Pure clarity win. |
| 8 | 9 IconPacks (F12) | 0 | Low | Low | Build/output size only. |
| 9 | Agent markers + `Trace` calls (F13) | ~40 | None | Trivial | Credibility. |
| 10 | `Flux.Web` + `flux-web-client` (F1) | **44,253** | **Product decision** | Low | Biggest single win in the repo, gated on confirming there are no web users. |

Subtotal excluding F1: **~12,300 lines**.
Subtotal including F1: **~56,500 lines**.

---

## 4. Simplify First — highest ROI refactors

Ordered by maintenance relief per unit of risk.

| # | Target | Change | Lines | Risk |
|---|---|---|---:|---|
| 1 | DTO + mapping layer (F4) | Delete AutoMapper/`PolyMapper`/`PolyDtoCache`/`BlockMapper`; use `[JsonDerivedType]` + explicit `record.From(...)` | −4,000 | Medium |
| 2 | Job orchestration (F3) | One `JobCommandService` in `Flux.Core`; UIs own only projection | −900 | Medium |
| 3 | Setting hierarchies (F5) | One `BlockValue(VariableType, object)`; delete `Setting` + 12 subclasses | −500 | Medium |
| 4 | Job monitor (F15) | Enum-discriminated `TriggeredAction`; one DTO | −200 (−800 with web) | Low |
| 5 | Shell micro-services (F11) | One `ShellServices.cs`, concrete types | −300 | Low |
| 6 | Repositories (F14) | Delete 7 `DbSet` wrappers, inject `FluxDbContext` | −150 | Low |

Subtotal: **~2,050 lines** (C# only; ~6,000 with the web DTO layer).

---

## 5. Estimated Totals

### Scenario A — web stack deleted (recommended)

| Metric | Before | After | Reduction |
|---|---:|---:|---:|
| First-party lines | 132,930 | 76,400 | **−42.5%** |
| C# lines | 104,099 | 47,550 | −54% |
| TS/HTML lines | 28,831 | 0 | −100% |
| `.csproj` projects | 23 | 10 | −57% |
| First-party projects | 11 | 9 | −18% |
| Interfaces | 115 | ~85 | −26% |
| NuGet dependencies (RuriLib + Flux.Native) | ~45 | ~32 | −29% |
| DI registrations (Flux.Native) | — | — | −8 services |
| Docker build stages | 2 (backend + frontend) | 0 | −100% |

### Scenario B — web stack kept, everything else fixed

| Metric | Before | After | Reduction |
|---|---:|---:|---:|
| First-party lines | 132,930 | 116,600 | **−12.3%** |
| C# lines | 104,099 | 87,800 | −16% |
| `.csproj` projects | 23 | 11 | −52% |
| Interfaces | 115 | ~95 | −17% |
| NuGet dependencies | ~52 | ~40 | −23% |

### Maintenance burden

| Dimension | Scenario A | Scenario B |
|---|---|---|
| Places a new `Job` field must be added | **1** (was 3: domain, `Flux.Shared` DTO, `Flux.Web` DTO + map) | 2 |
| Places a new block type must be added | **1** (was 4: block, `Flux.Web` DTO, `AutoMapperProfile`, `BlockMapper`) | 4 |
| Places a new primitive setting type must be added | **1** (was 4) | 1 |
| Browser stacks to upgrade | **1** (was 4) | 1 |
| Native dependencies to vendor | **0** (was 2 copies) | 0 |
| Build projects | **10** (was 23) | 11 |
| UIs to keep in sync | **1** (was 2) | 2 |

---

## 6. Sequencing

**Week 1 — pure deletion, no design decisions.** F9 (vendored TlsClient), F6 (PairTrading), F13 (markers), F8 (rename), F12 (icon packs), F2 dead services. Expect ~7,300 lines gone and a green build every day. Zero user-facing risk.

**Week 2 — decide F1.** Answer one question: does anyone use the web UI? If no, branch and delete — 44,253 lines. If yes, invert the plan: web becomes primary, desktop gets the same treatment. Do not leave it undecided; that is the current state and it costs 33% of the codebase.

**Weeks 3–4 — F7 + F10.** Delete three browser stacks (after grepping user configs) and the fast transpiler path. Both are single-commit changes with clear verification: existing tests plus a config-load smoke test.

**Weeks 5–8 — F4, F3, F5.** These are real refactors. Do them only after the deletion phase, because deletion shrinks the blast radius: if F1 lands, F4 disappears entirely and F3 halves.

**Never — Section 2.** The HTTP transport, proxy layer, parallelizer, and block registry are the product. Deleting them would remove the reason the tool exists.

---

## 7. The one-line verdict

`RuriLib.Http` + `RuriLib.Proxies` + `RuriLib.Parallelization` + the block registry + LoliCode round-trip is roughly **7,600 lines of genuinely hard, genuinely necessary code**. Everything else — 125,000 lines — is UI, DTO plumbing, and features nobody asked for. The ratio is upside down. Fix the ratio, not the files.

---

## Appendix — Evidence Index

| Claim | Evidence |
|---|---|
| Web dormant | `git log --since=2026-04-19 -- Flux.Web` → 5; `-- flux-web-client` → 1 |
| `Flux.Shared` single consumer | `grep -rl "Flux.Shared" --include=*.csproj` → only `Flux.Native.csproj` |
| Auth/Settings/Plugin dead | grep for `AuthenticationService`, `SettingsFacade`, `PluginService` in `Flux.Native` → only DI registration lines |
| Notification write-only | `grep -rn "StreamAsync"` → only `Flux.Shared.Tests/Notifications/NotificationServiceTests.cs:28` |
| 182 DTOs | `find Flux.Web/Dtos -name "*.cs" \| wc -l` → 182 |
| 90 maps | `grep -c "CreateMap" Flux.Web/Utils/AutoMapperProfile.cs` → 90 |
| PairTrading orphan | `grep -rn "PairTrading"` outside its folder → `BuiltInBlockRegistry.cs:58` only |
| Dual TlsClient | `RuriLib.csproj:61-62` — `ProjectReference` + `PackageReference` for same lib |
| Dual `RLHttpClient` | `RuriLib/Functions/Http/RLHttpClient.cs`, `RuriLib.Http/RLHttpClient.cs` |
| Empty `Setting` base | `RuriLib/Models/Blocks/Settings/Setting.cs` — 9 lines, no members |
| Agent markers | 5 × `#COMPLETION_DRIVE`, 5 × `#SUGGEST_VERIFY` in `.cs` files |
| 10 icon packs | `grep -c "IconPacks" Flux.Native/Flux.Native.csproj` → 10 |
