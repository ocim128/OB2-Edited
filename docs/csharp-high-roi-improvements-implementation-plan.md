# C# High-ROI Improvements Implementation Plan

## Purpose

Plan implementation for the C# improvements identified in the repository audit before changing production code.

This document is scoped to existing repository architecture:

- Runtime/block engine: `RuriLib/`
- Core persistence/domain: `Flux.Core/`
- Shared orchestration/projection: `Flux.Shared/`
- Desktop WPF app: `Flux.Native/`
- ASP.NET API/static host: `Flux.Web/`
- Tests: `RuriLib.Tests/`, `Flux.Shared.Tests/`, `RuriLib.Http.Tests/`

No implementation has started as part of this plan.

## Assumptions And Unknowns

- SQLite is the observed relational provider for `ApplicationDbContext`.
- `Flux.sln` remains the intended top-level build entry point, but targeted project builds may be used when solution-level state is unrelated.
- Desktop repository registrations in `Flux.Native/App.xaml.cs` are currently singleton while `ApplicationDbContext` is registered scoped. This plan avoids broad lifetime rewrites unless required by validation.
- Existing API response DTO shapes should remain backward-compatible.
- External clients may depend on current hit/proxy API routes and query parameters. Query behavior changes must preserve routes and response shapes.
- Safe patched package versions must be confirmed during implementation with `dotnet list package --vulnerable --include-transitive`.
- `Libraries/TlsClient.NET` is currently modified in the worktree. This plan does not require editing that vendored directory.
- Unknown: production-scale row counts for `Hits`, `Proxies`, `Records`, and `Wordlists`. Index choices should be validated against query shape, not guessed volumes.

## Current Architecture Notes

`RuriLib` owns automation/runtime execution and block implementations. `Flux.Core` owns EF entities, repositories, settings, jobs, and persistence helpers. `Flux.Shared` provides orchestration and dashboard/query services over core services. `Flux.Native` is the WPF desktop app and uses shared services plus repositories directly. `Flux.Web` exposes API controllers over the same repository/domain layer.

Database access flows through `ApplicationDbContext` and repository interfaces such as `IHitRepository`, `IProxyRepository`, `IJobRepository`, `IRecordRepository`, and `IWordlistRepository`. `Flux.Web` registers repositories as scoped. `Flux.Native` currently registers several repositories as singleton.

Runtime user-supplied logic includes regex parsing, wordlist rules, block inputs, and config execution. These paths should fail predictably and avoid unbounded CPU/memory behavior.

## Module Boundaries

- `Flux.Native`
  - Owns WPF viewmodels and desktop DI registration.
  - Should not gain web/API-specific dependencies.
- `Flux.Web`
  - Owns HTTP controller behavior and API contracts.
  - Should delegate durable persistence concerns to `Flux.Core`.
- `Flux.Core`
  - Owns EF entities, indexes, migrations, repositories, and domain helpers.
  - Shared dedupe/index behavior belongs here when used by Web and Native.
- `Flux.Shared`
  - Owns dashboard and app orchestration.
  - Should not reach into UI-specific code.
- `RuriLib`
  - Owns runtime block behavior and parsing helpers.
  - Runtime hardening should stay close to `RegexCache`, parsing functions, and block methods.

## Prioritized Phase Order

1. Baseline and scope gate.
2. Correctness quick wins in desktop and list blocks.
3. Hit query and dedupe improvements.
4. EF schema indexes.
5. Desktop dashboard EF concurrency mitigation.
6. Runtime regex hardening.
7. Dependency security patching.
8. Guardrails and follow-up validation.

Phases 1, 4, and 6 can be implemented independently. Phases 2 and 3 are related but intentionally separated so query behavior changes and schema/index changes can be reviewed independently.

## Phase 0. Baseline And Scope Gate

### Objective

Establish current build, test, dependency, and database-migration state before code changes.

### Scope

- Main C# projects and available test projects.
- Current NuGet vulnerability state.
- Current EF migration/model snapshot state.
- No behavior changes.

### Technical Tasks

- Capture worktree status:
  - `git status --short`
- Capture targeted build baseline:
  - `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
  - `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
  - `dotnet build RuriLib/RuriLib.csproj -c Debug --no-restore`
- Run available targeted tests:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
  - `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`
  - `dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj`
- Capture dependency vulnerability baseline:
  - `dotnet list Flux.sln package --vulnerable --include-transitive`
  - targeted project commands if solution-level restore is blocked.
- Inspect EF migration state:
  - `Flux.Core/Migrations/`
  - `Flux.Core/ApplicationDbContext.cs`

### Dependencies

- .NET 8 SDK.
- NuGet restore access for dependency audit.
- Existing local modifications must not be reverted.

### Risks/Blockers

- Solution-level commands may fail because of pre-existing solution metadata or missing assets.
- Tests may require package restore and generated runtime assets.
- Vulnerability command returns non-zero when vulnerabilities or missing assets exist.

### Deliverables

- Recorded baseline command results in implementation summary.
- Clear classification of pre-existing failures versus introduced failures.

### Validation/Testing Criteria

- Each baseline command has an observed result.
- Any skipped command has an explicit reason.

### Exit Criteria

- Implementation starts only after verification targets and known blockers are documented.

## Phase 1. Correctness Quick Wins

### Objective

Fix small, concrete runtime correctness bugs with minimal blast radius.

### Scope

- `Flux.Native/ViewModels/Data/HitsViewModel.cs`
- `Flux.Native/ViewModels/Shared/DebuggerViewModel.cs`
- `RuriLib/Blocks/Functions/ListFunctions/Methods.cs`
- Targeted tests in `RuriLib.Tests/` where practical.

### Technical Tasks

- Fix self-assignment in `HitsViewModel` constructor:
  - Current: `fluxSettingsService = fluxSettingsService ?? throw ...`
  - Target: `this.fluxSettingsService = fluxSettingsService ?? throw ...`
- Fix same pattern in `DebuggerViewModel` constructor.
- Fix `ZipLists(fill: true)` padding:
  - Current shorter-left calculation uses `list2.Count - list2.Count`.
  - Target shorter-left calculation uses `list2.Count - list1.Count`.
- Harden `ListToDictionary` malformed input handling:
  - For items without the separator, either skip the item or map key to empty string.
  - Prefer skip if existing block semantics imply only valid `key:value` pairs produce dictionary entries.
- Add focused `RuriLib.Tests` coverage for:
  - `ZipLists` fills shorter first list.
  - `ZipLists` fills shorter second list.
  - `ListToDictionary` does not throw on missing separator.
- Consider a narrow Native constructor test only if existing test harness can instantiate the viewmodels cheaply. Do not create a broad WPF test harness for this phase.

### Dependencies

- `RuriLib.Tests` can reference block methods.
- Native viewmodel constructor tests may require service construction that is not worth adding for a two-line fix.

### Risks/Blockers

- `ListToDictionary` behavior for separatorless items is not documented. Pick the least surprising behavior and encode it in a test.
- `DebuggerViewModel` self-assignment may already be masked by non-null defaults until a specific path uses the field.

### Deliverables

- Constructor field assignment fixes.
- List function fixes.
- Focused tests for list behavior.

### Validation/Testing Criteria

- `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
- `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
- Manual code review confirms no unrelated formatting/refactor changes.

### Exit Criteria

- Duplicate-hit deletion no longer depends on a null `fluxSettingsService` field.
- Debugger capture grouping no longer depends on a null `fluxSettingsService` field.
- List block edge cases are covered by tests.

## Phase 2. Hit Query And Dedupe Improvements

### Objective

Reduce high-cost hit API queries and eliminate hash-collision risk in duplicate-hit deletion.

### Scope

- `Flux.Web/Controllers/HitController.cs`
- `Flux.Core/Entities/HitEntity.cs`
- `Flux.Native/ViewModels/Data/HitsViewModel.cs`
- Optional shared helper in `Flux.Core` if both Web and Native use it.

### API/Contracts

Existing routes and DTO response shapes remain unchanged:

- `GET /hit/recent`
- `DELETE /hit/duplicates`
- Desktop duplicate deletion behavior

The semantic identity for duplicate hits remains:

- Ignore wordlist enabled: `(Data, ConfigName)`
- Ignore wordlist disabled: `(Data, ConfigName, WordlistName)`

### Data Flow

Current `/hit/recent` flow:

1. Build filtered query.
2. Query distinct config names.
3. For each config and date, run `CountAsync`.
4. Return dates and per-config daily counts.

Target flow:

1. Build filtered query.
2. Query the date range once using half-open boundaries: `h.Date >= start && h.Date < endExclusive`.
3. Materialize grouped counts.
4. Fill missing config/day pairs in memory.
5. Return same DTO shape.

### Technical Tasks

- Replace `HitEntity.GetHashCode(bool)` dedupe usage with a structural key.
- Add a small shared helper if it avoids duplicating key construction:
  - Example location: `Flux.Core/Models/Hits/HitDedupeKey.cs` or static helper near hit domain models.
- Update `HitController.DeleteDuplicates` to group by structural key instead of `GetHashCode`.
- Update `HitsViewModel.DeleteDuplicatesAsync` to use the same key logic.
- Update or deprecate `HitEntity.GetHashCode(bool)` only if no longer used; do not remove if external code still references it.
- Rewrite `HitController.GetRecent` to one grouped query.
- Keep date filtering index-friendly. Do not use `h.Date.Date` in the `Where` predicate.
- If SQLite/EF cannot translate the day grouping cleanly, query only the minimal columns for the bounded date range and group in memory rather than reintroducing per-config/per-day SQL queries.
- Clamp unreasonable `days` input to a safe range if no validator exists. Preserve current behavior for valid values.
- Add tests if a Web test harness exists. If not, add domain-level tests for dedupe helper in `Flux.Shared.Tests` or `RuriLib.Tests` only if references fit cleanly.

### Dependencies

- EF Core can translate the chosen day grouping with SQLite, or the bounded range can be materialized with minimal columns and grouped in memory.
- Existing `RecentHitsDto` shape remains unchanged.

### Risks/Blockers

- Date grouping translation can vary by provider. SQLite is observed, but verify against the local provider.
- Existing hash-based behavior may accidentally treat collisions as duplicates. Structural key intentionally changes that behavior.
- Deleting duplicates after materializing all hits remains costly. This phase fixes correctness first; bulk deletion optimization can follow.

### Deliverables

- Structural dedupe key implementation.
- Updated Web and Native duplicate deletion paths.
- One-query recent hit aggregation.
- Tests or documented test gap.

### Validation/Testing Criteria

- `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
- `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
- Dedupe test with two distinct hits that collide only by hash is not practical without constructing a collision; instead test key fields explicitly.
- Recent-hit aggregation test or SQLite smoke verifies counts across multiple configs/days and missing-day zeros.

### Exit Criteria

- Duplicate deletion no longer groups by `string.GetHashCode`.
- `/hit/recent` query count no longer scales with `configs * days`.
- API response shape is unchanged.

## Phase 3. EF Schema Indexes

### Objective

Add targeted indexes for observed hot queries without changing data model semantics.

### Scope

- `Flux.Core/ApplicationDbContext.cs`
- `Flux.Core/Migrations/`
- `Flux.Core/Migrations/ApplicationDbContextModelSnapshot.cs`

### Database/Schema Design

Candidate indexes based on observed query shapes:

- `HitEntity`
  - `(OwnerId, Type, Date)`
  - `(OwnerId, ConfigName, Date)`
  - `(Date)`
  - Consider `(ConfigName, Date)` for admin recent-hit aggregation.
- `RecordEntity`
  - Unique or non-unique `(ConfigId, WordlistId)`.
  - Unique is preferred if current data is clean; use non-unique first if existing duplicates are possible.
- `ProxyEntity`
  - `(Status, Ping)`
  - `(LastChecked)`
  - Group-related filtering currently uses navigation `Group.Id`; if EF exposes a shadow FK, confirm generated column name before indexing.
- `WordlistEntity`
  - Owner-related indexes only if queries show frequent owner filtering.

### Technical Tasks

- Add index configuration in `ApplicationDbContext.OnModelCreating`.
- Generate EF migration with existing project conventions.
- Inspect generated migration for SQLite-compatible SQL.
- Decide whether `RecordEntity` should enforce uniqueness:
  - If existing records can contain duplicates, first add a cleanup migration or use non-unique index.
  - Do not add a destructive cleanup unless explicitly approved.
- Validate indexes against queries in:
  - `HitController.FilteredQuery`
  - `HitController.GetRecent`
  - `ProxyController.FilteredQuery`
  - `JobManagerService.SaveRecordAsync`
  - `MultiRunJobOptionsViewModel` record lookup

### Dependencies

- EF Core tooling available.
- Current migrations apply cleanly to a local SQLite database.

### Risks/Blockers

- Unique record index can fail migration if existing duplicate records exist.
- Indexes improve reads but add small write overhead for hit/proxy insert/update paths.
- Incorrect shadow FK index names can produce invalid migrations.

### Deliverables

- Updated model configuration.
- New migration.
- Updated model snapshot.

### Validation/Testing Criteria

- `dotnet build Flux.Core/Flux.Core.csproj -c Debug --no-restore`
- Apply migrations to a disposable SQLite database.
- Existing tests pass.
- Optional: compare `EXPLAIN QUERY PLAN` for representative hit/proxy queries before and after.

### Exit Criteria

- Migration applies cleanly.
- Hot query filters/orderings have matching indexes.
- No existing data deletion is introduced without approval.

## Phase 4. Desktop Dashboard EF Concurrency Mitigation

### Objective

Prevent dashboard refresh from starting concurrent EF operations on shared desktop repository/context instances.

### Scope

- `Flux.Shared/Services/DashboardService.cs`
- `Flux.Native/App.xaml.cs` only if a minimal service lifetime adjustment is required.
- No broad repository lifetime rewrite in this phase.

### State Management

`HomeViewModel` calls `IDashboardService.GetDesktopSnapshotAsync` on a timer. The current implementation starts multiple EF count tasks concurrently, while Native registers repositories as singletons over a scoped `ApplicationDbContext`.

### Technical Tasks

- Replace concurrent EF count tasks with sequential awaits in `GetDesktopSnapshotAsync`.
- Keep independent non-EF work, such as plugin count, simple and synchronous unless profiling requires background execution.
- Pass `cancellationToken` through all repository count queries where APIs support it.
- Avoid `.Result` after `Task.WhenAll`; return local awaited values.
- Consider a follow-up task, not this phase, to align Native repository lifetimes with EF scoped usage.

### Dependencies

- Existing `DashboardService` must remain usable by both Native and Web/shared callers.
- `IConfigRepository.GetAllAsync()` remains async but may be disk-backed.

### Risks/Blockers

- Sequential counts may increase dashboard refresh latency slightly, but avoids intermittent failures.
- A full lifetime correction may uncover broader singleton viewmodel coupling and should not be mixed into this small mitigation.

### Deliverables

- Safer `GetDesktopSnapshotAsync` implementation.
- No API or DTO shape changes.

### Validation/Testing Criteria

- `dotnet build Flux.Shared/Flux.Shared.csproj -c Debug --no-restore`
- `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
- Manual desktop smoke if available: Home dashboard refresh does not log EF concurrency exceptions.

### Exit Criteria

- `GetDesktopSnapshotAsync` no longer starts multiple EF queries concurrently against injected repositories.
- Cancellation and timeout behavior from `HomeViewModel` remains intact.

## Phase 5. Runtime Regex Hardening

### Objective

Prevent unbounded regex CPU usage and cache growth in runtime/config paths.

### Scope

- `RuriLib/Functions/Parsing/RegexCache.cs`
- `RuriLib/Functions/Parsing/RegexParser.cs`
- `RuriLib/Models/Data/Rules/RegexDataRule.cs`
- `RuriLib/Models/Environment/WordlistType.cs`
- `RuriLib/Models/Environment/EnvironmentSettings.cs`
- `RuriLib/Blocks/Utility/Methods.cs`

### Security Considerations

Regex patterns may come from configs, wordlist types, data rules, or block inputs. Treat these as user-controlled runtime inputs. Regex execution should have a timeout and predictable failure behavior.

### Performance Considerations

Compiled regex caching improves hot-path performance, but unbounded pattern caches can grow with dynamic/user-generated patterns. Add bounds without changing common static-pattern behavior.

### Technical Tasks

- Add a default regex timeout constant to `RegexCache`, for example 2 seconds unless existing runtime settings provide a better value.
- Change cached regex creation to use the constructor overload with `matchTimeout`.
- Include timeout in the cache key if configurable.
- Add a conservative max cache size or opportunistic clear strategy.
  - Keep it simple: if count exceeds threshold, clear the relevant cache.
  - Avoid implementing an LRU cache unless profiling proves needed.
- Route direct calls through `RegexCache` where patterns are reused or user-controlled:
  - `RegexDataRule.IsSatisfied`
  - `EnvironmentSettings.RecognizeWordlistType`
  - `WaitClipboard` regex branch
- Handle `RegexMatchTimeoutException` by call-site semantics, not a blanket catch:
  - predicate paths such as data rules return unsatisfied;
  - polling paths such as clipboard wait continue until the outer timeout;
  - parsing/block paths add context and fail the block rather than silently returning no match.
- Preserve existing successful match semantics.

### Dependencies

- .NET regex timeout overloads are available.
- Existing tests may need adjustment if they assume pathological regexes run indefinitely.

### Risks/Blockers

- A timeout can change behavior for intentionally expensive valid regexes.
- Cache clearing can temporarily reduce performance if users generate many unique patterns.
- Runtime settings do not currently expose regex timeout configuration; adding a setting would increase scope and should be avoided initially.

### Deliverables

- Timeout-enabled regex cache.
- Updated user-controlled regex call sites.
- Tests for timeout behavior and cached normal behavior.

### Validation/Testing Criteria

- `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
- Add a test with a known catastrophic pattern and assert it fails predictably within bounded time.
- Add normal regex parsing tests to guard existing behavior.

### Exit Criteria

- User-controlled regex execution has a bounded timeout.
- Cache growth has a simple upper bound.
- Existing regex parsing behavior remains intact for normal patterns.

## Phase 6. Dependency Security Patching

### Objective

Remove known vulnerable package versions from the main C# dependency graph.

### Scope

- `RuriLib/RuriLib.csproj`
- `Flux.Web/Flux.Web.csproj`
- `Flux.Native/Flux.Native.csproj`
- Package lock files if restore updates them.
- No application logic changes unless required by package API breaks.

### Security Considerations

Local vulnerability checks reported:

- `MailKit 4.8.0` top-level in `RuriLib`
- `MimeKit 4.8.0` transitive
- `AutoMapper 13.0.1` top-level in `Flux.Web`
- `Microsoft.Extensions.Caching.Memory 8.0.0` transitive
- `System.Text.Json 8.0.4` transitive
- `HtmlSanitizer 8.1.866-beta` warning during Native build

### Technical Tasks

- Run current vulnerability audit after restore:
  - `dotnet list Flux.sln package --vulnerable --include-transitive`
- Upgrade direct references first:
  - `MailKit` in `RuriLib/RuriLib.csproj`
  - `AutoMapper` in `Flux.Web/Flux.Web.csproj`
  - `HtmlSanitizer` in `Flux.Native/Flux.Native.csproj`
- If vulnerable transitive packages remain, add direct patched package references only where needed.
- Refresh lock files with restore.
- Build affected projects.
- Do not update unrelated packages in the same change.

### Dependencies

- NuGet network access.
- Compatible patched versions exist for .NET 8.

### Risks/Blockers

- Package major-version upgrades may include API breaks, especially AutoMapper.
- Direct transitive pinning can add maintenance burden. Prefer upgrading parents first.
- Lock file churn can be broad; keep package changes focused.

### Deliverables

- Updated package references and lock files.
- Vulnerability audit output showing resolved or explicitly accepted advisories.

### Validation/Testing Criteria

- `dotnet restore Flux.sln`
- `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
- `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
- `dotnet build RuriLib/RuriLib.csproj -c Debug --no-restore`
- `dotnet list Flux.sln package --vulnerable --include-transitive`

### Exit Criteria

- No known vulnerable packages remain in the audited main graph, or remaining items are documented with a blocker.
- No unrelated package upgrades are included.

## Phase 7. Guardrails And Follow-Up Validation

### Objective

Add lightweight safeguards so the same classes of issues are easier to catch later.

### Scope

- Build/test documentation or existing CI config if present.
- Minimal analyzer/build guard changes only if low-noise.
- No broad style enforcement.

### Observability/Logging

Existing runtime logging uses application loggers and debug output. This phase should not introduce a new logging stack. Only add logs where they clarify failure handling in changed paths, for example regex timeout diagnostics or dependency audit notes in CI output.

### Technical Tasks

- Consider enabling a focused analyzer rule for self-assignment if available without high warning noise.
- Add tests near changed behavior rather than broad snapshot tests.
- Document manual verification commands in the implementation summary.
- Consider adding dependency audit to CI if CI config already exists and the command is stable after restore.

### Dependencies

- Existing CI workflow availability under `.github/`.
- Analyzer changes must be evaluated against current suppressed warnings in `Directory.Build.props`.

### Risks/Blockers

- Enabling broad analyzers can create large unrelated warning noise.
- CI changes can fail on vulnerability advisories outside the current scope if package restore differs by environment.

### Deliverables

- Minimal guardrails only if they have low noise.
- Updated implementation summary with verification commands.

### Validation/Testing Criteria

- CI-related changes run locally where possible.
- No broad warning churn.

### Exit Criteria

- Changed behavior has tests or explicit manual validation.
- Future recurrence risk is reduced without introducing noisy process overhead.

## Rollback Strategy

- Phase 1 rollback: revert small source/test changes in Native/RuriLib files.
- Phase 2 rollback: revert controller/helper changes; API routes and DTOs are unchanged, so rollback is isolated.
- Phase 3 rollback: create a reverse migration if indexes have been applied to an existing database; source revert alone is not sufficient after deployment.
- Phase 4 rollback: restore previous dashboard concurrency if sequential counts cause unacceptable latency.
- Phase 5 rollback: restore direct regex behavior if timeout breaks legitimate configs; keep tests documenting the regression before deciding.
- Phase 6 rollback: revert package refs and lock files if patched versions break runtime behavior.

## Edge Cases And Failure Handling

- Duplicate-hit deletion must not delete distinct records due to hash collision.
- `/hit/recent` must return zero counts for config/day pairs with no hits.
- Regex timeout should produce a controlled result based on call-site semantics, not crash the process or silently hide block parsing errors.
- Dashboard cancellation from `HomeViewModel` should still stop long DB refreshes.
- Existing databases may already contain duplicate `RecordEntity` rows; unique indexing requires validation before enforcement.
- Package audits may report advisories in transitive dependencies after parent upgrades; direct pins are acceptable only when parent upgrades do not resolve them.

## Final Validation Matrix

- Correctness:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
  - targeted tests for list and regex behavior
- Shared/Core:
  - `dotnet build Flux.Core/Flux.Core.csproj -c Debug --no-restore`
  - `dotnet build Flux.Shared/Flux.Shared.csproj -c Debug --no-restore`
- Desktop:
  - `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
  - manual Home dashboard smoke if WPF launch is available
- Web:
  - `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
  - manual or test verification for `/hit/recent` and duplicate deletion
- Security:
  - `dotnet list Flux.sln package --vulnerable --include-transitive`
- Database:
  - apply EF migrations to disposable SQLite database
  - inspect generated migration for non-destructive index operations

## Implementation Stop Conditions

- Any change requires deleting or transforming existing user data without explicit approval.
- EF migration generation proposes table rebuild or destructive operations beyond index creation.
- A package upgrade requires non-trivial API migration outside the vulnerable package boundary.
- Native repository lifetime changes cascade into broad viewmodel/service construction changes.
- Tests fail in areas unrelated to touched code and cannot be classified as pre-existing.
