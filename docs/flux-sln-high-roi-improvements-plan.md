# Flux.sln High-ROI Engineering Improvements Plan

## Purpose

Plan implementation for the low-risk, high-ROI improvements identified in the `Flux.sln` audit before changing production code.

This plan is scoped to the existing repository architecture:

- Solution/build: `Flux.sln`, `Directory.Build.props`, `Directory.Build.targets`, project files.
- Runtime/core persistence: `Flux.Core/`, `Flux.Shared/`, `RuriLib/`.
- Desktop shell: `Flux.Native/`.
- Web API and static web host: `Flux.Web/`.
- Web frontend build inputs: `flux-web-client/`.
- Tests: `RuriLib.Tests/`, `RuriLib.Http.Tests/`, `Flux.Shared.Tests/`.

This plan does not introduce new services, microservices, queues, infrastructure, or a new persistence model.

## Implementation Status

Planning only. No implementation has been started.

## Assumptions And Unknowns

- `Flux.sln` is intended to be the main build/test entry point.
- Missing solution projects are stale entries unless the user provides missing source folders:
  - `Flux.Native.Updater/Flux.Native.Updater.csproj`
  - `Flux.Web/Flux.Web.Api/Flux.Web.Api.csproj`
  - `Flux.Web.Api.Tests/Flux.Web.Api.Tests.csproj`
- Existing `Flux.Web/Flux.Web.csproj` is the current ASP.NET API project visible on disk.
- `Flux.Web/wwwroot` is generated output and ignored by `Flux.Web/.gitignore`; frontend asset changes belong in `flux-web-client/angular.json`, not generated output.
- SQLite remains the only observed relational provider for `ApplicationDbContext`.
- Unknown: whether external clients depend on `pageNumber=0`. To minimize breakage, pagination should clamp `0` to `1` instead of returning `400` in the first pass.
- Unknown: exact safe patched versions for vulnerable packages must be confirmed during implementation with `dotnet list package --vulnerable --include-transitive` after restore.
- Unknown: `Flux.Native` DbContext/repository lifetimes may have user-facing coupling in singleton viewmodels. Treat broad lifetime changes as a separate step; dashboard query sequencing is only a mitigation, not a complete lifetime fix.

## Current Architecture Notes

`RuriLib` owns automation/runtime execution. `Flux.Core` owns EF entities, repositories, settings, jobs, and persistence helpers. `Flux.Shared` provides app-level orchestration/projection over core services. `Flux.Native` is the WPF desktop app and uses shared services plus repositories directly. `Flux.Web` is the ASP.NET API/static host. `flux-web-client` builds Angular static assets consumed by `Flux.Web`.

Database access currently flows through `ApplicationDbContext` and repository interfaces such as `IHitRepository`, `IProxyRepository`, `IJobRepository`, and `IWordlistRepository`. Web API repositories are scoped in `Flux.Web/Program.cs`. Desktop currently registers several repositories as singletons in `Flux.Native/App.xaml.cs` while the DbContext registration is scoped.

Job runtime state is held by `JobManagerService` and runtime job objects. Web SignalR services track job-to-connection mappings and subscribe to job events.

## Prioritized Phase Order

1. Restore solution build integrity.
2. Add API pagination guardrails.
3. Optimize recent-hit aggregation and add targeted indexes.
4. Fix SignalR job connection tracking and event cleanup.
5. Reduce web static asset payload.
6. Patch vulnerable NuGet packages.
7. Reduce bulk operation materialization.
8. Address desktop EF lifetime/concurrency risk.
9. Tighten validation, analyzer, and CI guardrails.

Phase 7 and Phase 8 are intentionally later because they have more behavioral and lifetime risk than the earlier quick wins.

## Phase 0. Baseline And Scope Gate

### Objective

Establish a verifiable baseline and confirm which improvements can be implemented without unrelated refactors.

### Scope

- `Flux.sln`
- Project files and lock files.
- Existing tests and build commands.
- No product behavior changes.

### Technical Tasks

- Capture current build status:
  - `dotnet restore Flux.sln`
  - `dotnet build Flux.sln -c Debug --no-restore`
- Capture targeted test availability:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
  - `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`
  - `dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj`
- Capture dependency audit:
  - `dotnet list Flux.sln package --vulnerable --include-transitive`
- Measure web asset baseline:
  - size of `flux-web-client/dist` after build
  - size of Monaco assets copied by Angular
- Record stale solution entries and missing project paths.

### Dependencies

- .NET 8 SDK installed.
- NuGet restore access.
- Node/npm only needed before frontend payload validation.

### Risks/Blockers

- Current `Flux.sln` fails before full build verification because of missing project entries.
- Existing uncommitted vendored `Libraries/TlsClient.NET` state must not be reverted.

### Deliverables

- Baseline command results noted in implementation summary.
- Confirmed list of stale solution entries.
- Confirmed list of vulnerable packages after restore.

### Validation/Testing Criteria

- Baseline commands are run and failures are classified as pre-existing or introduced.

### Exit Criteria

- Implementation can start with a known verification target and known blockers.

## Phase 1. Restore Solution Build Integrity

### Objective

Make `Flux.sln` a reliable build/test entry point again.

### Scope

- `Flux.sln`
- Only project inclusion/configuration metadata.
- No source code behavior changes.

### Technical Tasks

- Remove or correct stale project entries:
  - `Flux.Native.Updater`
  - `Flux.Web.Api`
  - `Flux.Web.Api.Tests`
- Replace the stale `Flux.Web.Api` project entry with existing on-disk `Flux.Web/Flux.Web.csproj`.
- If the existing `Flux.Web` solution folder conflicts with adding a `Flux.Web` project name, remove or rename the solution folder rather than inventing a nested API project.
- Do not create new `Flux.Native.Updater`, `Flux.Web.Api`, or `Flux.Web.Api.Tests` projects in this phase.
- Add missing `Debug|Any CPU` and `Release|Any CPU` mappings for:
  - `RuriLib.Http`
  - `RuriLib.Parallelization`
- Run `dotnet sln Flux.sln list` and ensure all listed project paths exist.

### Dependencies

- Decision on stale entries. Default assumption: remove stale entries if source folders do not exist.

### Risks/Blockers

- If missing projects are expected but omitted from the workspace, removing them could hide a packaging/deployment flow. This is unknown and must be surfaced before implementation if the user expects those projects.

### Deliverables

- Updated `Flux.sln`.
- No missing project file errors from solution build.

### Validation/Testing Criteria

- `dotnet sln Flux.sln list` shows only existing project files.
- `dotnet restore Flux.sln` completes past solution metadata.
- `dotnet build Flux.sln -c Debug --no-restore` no longer emits `MSB3202` for missing projects.
- No `MSB4121` warnings for `RuriLib.Http` or `RuriLib.Parallelization`.

### Exit Criteria

- Solution-level restore/build can proceed to source/package issues instead of failing on solution metadata.

## Phase 2. API Pagination Guardrails

### Objective

Prevent invalid pagination offsets and uncontrolled page sizes in existing API list endpoints.

### Scope

- `Flux.Web/Dtos/Common/PaginationDto.cs`
- `Flux.Web/Models/Pagination/PagedList.cs`
- Existing API consumers of `PagedList<T>`, especially hit/proxy list endpoints.

### API/Contracts

Current contract remains query-string based. First pass should be backward-compatible:

- Treat `pageNumber <= 0` as page `1`.
- Clamp `pageSize` to a bounded range.
- Preserve response shape: `Items`, `PageNumber`, `TotalPages`, `PageSize`, `TotalCount`.

### Technical Tasks

- Change `PaginationDto.PageNumber` default from `0` to `1`.
- Add normalization in `PagedList.CreateAsync`:
  - `pageNumber = Math.Max(1, pageNumber)`
  - `pageSize = Math.Clamp(pageSize, 1, MaxPageSize)`
- Use a constant for `MaxPageSize`, initially `500` unless existing UI requires more.
- Pass cancellation tokens through `CountAsync` and `ToListAsync` if call sites already have them available.
- Add focused tests for:
  - default page returns first page
  - `pageNumber=0` behaves like page `1`
  - oversized `pageSize` is capped
- Do not create a new web test project only for this small change. If no existing test project can reference `PagedList<T>` cleanly, document the test gap and validate through a temporary SQLite-backed smoke or manual API smoke.

### Dependencies

- Solution build fixed enough to compile `Flux.Web` or the relevant test project.
- Existing test project for `Flux.Web` is currently missing from solution and disk.

### Risks/Blockers

- Unknown client dependence on `pageNumber=0`; compatibility clamp mitigates this.
- No current web API test project is present on disk.

### Deliverables

- Pagination normalization.
- Tests or documented test gap if no suitable test project exists.

### Validation/Testing Criteria

- Build `Flux.Web/Flux.Web.csproj`.
- Run available tests.
- Manual verification with `PagedList.CreateAsync` unit coverage if test harness exists.

### Exit Criteria

- No endpoint can generate negative SQL skip from pagination inputs.
- API response shape remains unchanged.

## Phase 3. Recent Hits Aggregation And Database Indexes

### Objective

Replace N+1 recent-hit statistics queries with a single grouped query and add indexes for common hit/proxy access patterns.

### Scope

- `Flux.Web/Controllers/HitController.cs`
- `Flux.Core/ApplicationDbContext.cs`
- New EF Core migration under `Flux.Core/Migrations/`
- SQLite schema only, via EF migrations.

### Data Flow

Current `GetRecent(days)` flow:

1. Apply owner/type filter.
2. Query distinct config names.
3. For each config and each day, run a separate `CountAsync`.

Target flow:

1. Apply owner/type/date filter.
2. Group by `ConfigName` and day.
3. Materialize grouped counts once.
4. Fill missing day buckets in memory to preserve DTO shape.

### Database/Schema Design

Add targeted indexes only where existing query patterns justify them:

- `HitEntity`: `(Type, Date)` for admin recent-hit queries.
- `HitEntity`: `(OwnerId, Type, Date)`
- `HitEntity`: `(ConfigName, Date)`

Do not add broad indexes on large text fields such as `Data`, `CapturedData`, or `Proxy` because existing search uses `%term%` and normal B-tree indexes will not help SQLite for leading wildcard searches.
Do not add proxy indexes in this phase unless the generated EF model exposes a concrete FK path; proxy query optimization belongs with Phase 7 if bulk proxy operations are changed.

### Technical Tasks

- Refactor `GetRecent(int days)` to one grouped query.
- Clamp `days` to a reasonable maximum, for example `1..365`, preserving existing response type.
- Use a fixed `startDate` and `today` computed once to avoid inconsistent `DateTime.UtcNow` calls during execution.
- Add EF model indexes in `OnModelCreating`.
- Generate migration from `Flux.Core`.
- Verify generated migration does not alter unrelated tables.
- Add focused test coverage only if a web/controller test harness exists. Do not extract a production helper solely to make this endpoint unit-testable.

### Dependencies

- Phase 1 solution build.
- EF tooling available through existing `Microsoft.EntityFrameworkCore.Design` references.

### Risks/Blockers

- `Date.Date` translation must be verified for SQLite. If translation is poor, use date range grouping by formatted date or project to date string via supported SQLite translation.
- Adding indexes has a small write-cost tradeoff for hit insertion.

### Deliverables

- `GetRecent` performs one grouped DB query.
- EF migration for indexes.
- Tests or documented manual verification.

### Validation/Testing Criteria

- `dotnet build Flux.Core/Flux.Core.csproj -c Debug --no-restore`
- `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
- Verify migration script.
- Run available tests.
- Optional local SQLite explain/query timing before and after if a populated database is available.

### Exit Criteria

- Query count is independent of `configs * days`.
- Existing `RecentHitsDto` response shape is preserved.

## Phase 4. SignalR Job Connection Tracking

### Objective

Make job SignalR connection tracking thread-safe and clean up event subscriptions when no clients remain.

### Scope

- `Flux.Web/Services/MultiRunJobService.cs`
- `Flux.Web/Services/ProxyCheckJobService.cs`
- `Flux.Web/SignalR/JobHub.cs`
- No changes to runtime job event contracts.

### State Management

Current state:

- Singleton services hold `Dictionary<Job, List<string>>`.
- Job events call back into the same service and read connection lists.
- Register/unregister can run concurrently with event delivery.

Target state:

- Track by stable `job.Id`, not object identity where possible.
- Use a private lock and a small per-service registry entry containing the runtime job reference plus a `HashSet<string>` of connection IDs.
- Notify clients from a snapshot of connection IDs.
- Detach job event handlers when the last connection for a job is removed.
- Enforce the same ownership boundary as HTTP job endpoints: admin can connect to any job; guest can connect only to jobs with matching `OwnerId`.

### Technical Tasks

- Introduce a private lock around registry mutation.
- Prevent duplicate connection IDs for the same job.
- Expose the authenticated `ApiUser` from `AuthorizedHub` to `JobHub` through a protected property, or pass it to the job service from `JobHub`.
- Before registering the connection, reject guest access to jobs not owned by that guest.
- In unregister:
  - tolerate missing job or missing connection
  - remove empty job entries
  - unsubscribe job event handlers when entry is removed
- In `NotifyClientsAsync`:
  - return if sender/job is missing
  - snapshot connection IDs before `SendAsync`
  - avoid indexing dictionary with absent keys
- In `JobHub.GetJobId`, replace raw `int.Parse` with safe parse and consistent API error.
- Make `OnDisconnectedAsync` tolerate partial/failed connection setup.

### Dependencies

- No new infrastructure.
- Existing SignalR hubs remain unchanged externally.

### Risks/Blockers

- Need to preserve current behavior where multiple clients can observe the same job.
- Unsubscribing too early would drop updates for remaining clients, so empty-entry logic must be tested.
- Existing hub authorization stores the user in a private property; implementation must expose it narrowly without changing token validation semantics.

### Deliverables

- Thread-safe connection registry.
- Job ownership enforcement for SignalR job connections.
- Last-client cleanup.
- Safer disconnect handling.

### Validation/Testing Criteria

- Unit test registry behavior if helper extraction is small and non-invasive.
- Manual or integration check:
  - connect two clients to same job
  - disconnect one
  - remaining client still receives updates
  - disconnect last
  - job event handlers are detached
  - guest cannot subscribe to another guest's job
- `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`.

### Exit Criteria

- No mutable dictionary/list access occurs without synchronization or snapshotting.
- Missing/invalid disconnect data does not throw from hub cleanup.

## Phase 5. Web Static Asset Payload Reduction

### Objective

Reduce generated web static assets by copying only required Monaco runtime files.

### Scope

- `flux-web-client/angular.json`
- Generated `Flux.Web/wwwroot` only for validation, not source edits.
- No changes to Monaco editor usage unless required to preserve runtime path.

### Deployment/Infrastructure

Observed Angular config copies all of `node_modules/monaco-editor` into `/assets/monaco/`. Local generated Monaco assets are about 88 MB. `Flux.Web` serves static files via `UseStaticFiles`.

### Technical Tasks

- Replace the broad Monaco asset glob with a narrow copy of runtime assets.
- Preserve or explicitly configure the path expected by `ngx-monaco-editor-v2`.
- Candidate initial config:
  - input: `node_modules/monaco-editor/min`
  - output: `/assets/monaco/min/`
  - glob: `**/*`
- If the runtime loader path changes, set `baseUrl` in `NgxMonacoEditorConfig` instead of copying the full package back.
- Apply the same narrowing to the Angular `test` asset list only if test builds copy Monaco assets.
- Build frontend and verify editor loads.
- Confirm no references require `dev`, `esm`, or source-map folders.

### Dependencies

- `npm install` in `flux-web-client` if `node_modules` is absent or stale.
- Frontend build must remain compatible with Angular builder currently configured.

### Risks/Blockers

- `ngx-monaco-editor-v2` may assume a base path containing `min/vs`. Validate in browser.
- Source maps are useful for frontend debugging; development builds may keep broader assets only if a concrete debugging need is confirmed.

### Deliverables

- Updated `flux-web-client/angular.json`.
- Build output size comparison.

### Validation/Testing Criteria

- `cd flux-web-client; npm run build`
- Verify generated `dist/assets/monaco` contains required runtime files.
- Smoke test code editor page in browser or via existing app route.

### Exit Criteria

- Monaco editor loads and language registration still works.
- Generated static asset size is materially reduced.

## Phase 6. Dependency Security Patch Pass

### Objective

Remove known vulnerable NuGet package versions with minimal code churn.

### Scope

- Direct package references in:
  - `RuriLib/RuriLib.csproj`
  - `Flux.Native/Flux.Native.csproj`
  - `Flux.Core/Flux.Core.csproj`
  - `Flux.Shared/Flux.Shared.csproj` if explicit transitive pinning is needed
- `packages.lock.json` files if restore updates them.
- Do not edit vendored `Libraries/TlsClient.NET` unless required by a direct Flux build failure.

### Security Considerations

Known audit findings from the baseline pass included:

- `MailKit 4.8.0`
- `MimeKit 4.8.0`
- `Microsoft.Extensions.Caching.Memory 8.0.0`
- `HtmlSanitizer 8.1.866-beta`

Patch versions must be chosen from NuGet restore/audit output during implementation.

### Technical Tasks

- Run package audit after Phase 1 restore.
- Upgrade direct package references first.
- If a vulnerable transitive package remains, add explicit patched top-level reference in the nearest owning project.
- Avoid broad framework/package upgrades unrelated to audit findings.
- Update lock files through normal restore.

### Dependencies

- NuGet network access.
- Solution restore/build fixed enough to evaluate all projects.

### Risks/Blockers

- `HtmlSanitizer` beta-to-stable may include behavior changes; smoke test markdown/HTML rendering surfaces.
- `MailKit`/`MimeKit` changes may affect mail blocks; run relevant RuriLib tests if present.
- `Microsoft.Extensions.Caching.Memory` may be transitive from EF or ASP.NET packages; explicit pinning may be the minimal fix.

### Deliverables

- Updated package references/lock files.
- Clean or reduced `dotnet list package --vulnerable --include-transitive` output.

### Validation/Testing Criteria

- `dotnet restore Flux.sln`
- `dotnet build Flux.sln -c Debug --no-restore`
- `dotnet list Flux.sln package --vulnerable --include-transitive`
- Targeted tests for touched packages where available.

### Exit Criteria

- No known high-severity package finding remains unless explicitly documented as transitive/unresolvable in this repo.

## Phase 7. Bulk Database Operation Efficiency

### Objective

Avoid loading large entity sets into memory for simple bulk delete/update operations.

### Scope

- `Flux.Web/Controllers/HitController.cs`
- `Flux.Web/Controllers/ProxyController.cs`
- `Flux.Core/Repositories/DbProxyRepository.cs`
- Repository interfaces only if necessary.

### Data Flow

Current bulk paths frequently do:

1. Build filtered `IQueryable`.
2. `ToListAsync`.
3. `RemoveRange` or update entities.
4. `SaveChangesAsync`.

Target paths should use EF Core set-based operations where behavior is simple:

- `ExecuteDeleteAsync` for filtered deletes.
- `ExecuteUpdateAsync` for simple proxy group move if the FK can be set directly.

### Technical Tasks

- Convert hit/proxy filtered deletes to server-side delete where no per-entity behavior is required.
- Do not call `ExecuteDeleteAsync` on list queries that include sorting or eager-loading. Create mutation-specific query builders that apply only ownership and filter predicates.
- Return affected count from `ExecuteDeleteAsync`.
- For proxy move:
  - confirm concrete FK/shadow FK name for `ProxyEntity.Group`
  - prefer explicit FK property if adding it is safe and migration impact is understood
  - otherwise defer move optimization
- For duplicate proxy removal:
  - keep current implementation initially if SQL rewrite becomes complex
  - consider unique index plus conflict handling as separate work, not this phase

### Dependencies

- EF Core 8 supports `ExecuteDeleteAsync` and `ExecuteUpdateAsync`.
- Query filters must remain ownership-safe before executing set-based operations.
- Mutation queries must not reuse API list queries that add `Include`, `ThenInclude`, or `OrderBy`.

### Risks/Blockers

- Set-based operations bypass tracked entity events. No entity hooks were observed, but verify repository assumptions before applying.
- Proxy move may require FK modeling changes; avoid if migration risk exceeds ROI.
- Accidentally executing a set-based delete against an ordered/list query can fail translation or carry unnecessary joins.

### Deliverables

- Server-side bulk delete for safe endpoints.
- Optional proxy move optimization only if low risk after inspection.

### Validation/Testing Criteria

- Test affected row count matches previous behavior.
- Ownership-filtered delete cannot affect another guest's rows.
- Build and available tests pass.

### Exit Criteria

- Large filtered deletes no longer materialize full rows.
- Any deferred bulk path is explicitly documented with reason.

## Phase 8. Desktop EF Lifetime And Dashboard Concurrency

### Objective

Reduce one observed desktop DbContext concurrency hotspot and document the remaining lifetime issue without destabilizing WPF viewmodel lifetimes.

### Scope

- `Flux.Native/App.xaml.cs`
- `Flux.Shared/Services/DashboardService.cs`
- Repository lifetime registration and dashboard query flow.

### Module Boundaries

`Flux.Native` composes the desktop service provider. `Flux.Shared` owns dashboard projections but currently receives repositories directly. Any broad repository lifetime correction must respect singleton WPF viewmodels and shared services.

### Technical Tasks

First pass:

- Remove parallel EF queries from `DashboardService.GetDesktopSnapshotAsync` because desktop repositories may share a root-scoped context.
- Replace `Task.WhenAll` over repository queries with sequential awaits.
- Avoid `.Result` after `Task.WhenAll`; use awaited values.
- State clearly in the implementation summary that this does not fix all root-scoped DbContext usage in `Flux.Native`.

Second pass, only after first pass is verified:

- Evaluate changing desktop repository access from singleton repositories over scoped DbContext to scope/factory-backed access.
- Do not change repository lifetimes in the second pass until direct singleton viewmodel/service consumers are listed.
- Enable DI scope validation in development if possible.
- Identify singleton viewmodels that directly depend on repositories and decide whether they should use scope factories.

### Dependencies

- Phase 1 build.
- Desktop smoke test ability.

### Risks/Blockers

- Broad lifetime changes can ripple through singleton WPF viewmodels.
- Sequential dashboard queries may slightly increase dashboard refresh latency but reduce intermittent failures.
- Other singleton services/viewmodels may still access long-lived DbContext instances after the first pass.

### Deliverables

- Safer dashboard refresh implementation.
- Explicit follow-up item for full `Flux.Native` repository/DbContext lifetime correction.

### Validation/Testing Criteria

- Desktop starts.
- Home dashboard refresh succeeds repeatedly.
- Repeated dashboard refresh does not introduce EF concurrent operation exceptions. If concurrent job writes still reproduce a failure, document it under the remaining root-scoped DbContext risk.
- `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`.

### Exit Criteria

- Known parallel access inside dashboard refresh is removed.
- Remaining root-scoped DbContext risk is either fixed in the second pass or explicitly documented as deferred.

## Phase 9. Build Quality Gates And Warning Hygiene

### Objective

Make future regressions easier to catch without turning the entire legacy warning backlog into a blocker.

### Scope

- `Directory.Build.props`
- `Directory.Build.targets`
- Project-specific analyzer settings.
- Optional CI workflow if added later.

### Technical Tasks

- Do not globally enable warnings-as-errors immediately.
- Create a targeted warning baseline plan:
  - remove broad suppressions only from projects touched in this work
  - fix high-value warnings in touched files
  - keep vendored `Libraries/TlsClient.NET` suppressions isolated
- Consider adding a lightweight local CI script or documented command set:
  - restore
  - build solution
  - targeted tests
  - vulnerable package audit
- If GitHub Actions is desired later, add it as a separate small infrastructure phase.

### Dependencies

- Phase 1 must make solution build meaningful.

### Risks/Blockers

- Current solution emits many pre-existing warnings. A broad warnings-as-errors switch would block unrelated work.
- Analyzer output differs between projects because some explicitly disable analyzers.

### Deliverables

- Minimal quality gate documentation or script.
- No broad warning policy changes unless the warning count is first reduced.

### Validation/Testing Criteria

- Gate commands run locally.
- No new warning policy blocks existing build unexpectedly.

### Exit Criteria

- Future implementation passes have a documented validation path.

## Rollback Strategy

- Phase 1: revert `Flux.sln` metadata only.
- Phase 2: revert pagination normalization; API response DTO shape is unchanged, so rollback is localized.
- Phase 3: revert controller query change and drop generated migration if not applied. If migration was applied locally, create a down migration or run EF migration rollback.
- Phase 4: revert SignalR service changes; no persisted data affected.
- Phase 5: revert `angular.json`; regenerate frontend assets.
- Phase 6: revert package version changes and lock files.
- Phase 7: revert set-based operations to materialized repository methods.
- Phase 8: revert dashboard query/lifetime changes; no schema impact.

## Cross-Phase Validation Matrix

- Solution integrity:
  - `dotnet sln Flux.sln list`
  - `dotnet restore Flux.sln`
  - `dotnet build Flux.sln -c Debug --no-restore`
- Runtime tests:
  - `dotnet test RuriLib.Tests/RuriLib.Tests.csproj`
  - `dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj`
  - `dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj`
- Web API:
  - `dotnet build Flux.Web/Flux.Web.csproj -c Debug --no-restore`
  - manual smoke for hits/proxies pagination if no web test project exists
- Desktop:
  - `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
  - start app and verify Home dashboard refresh
- Frontend:
  - `cd flux-web-client`
  - `npm run build`
  - verify Monaco editor route loads
- Security:
  - `dotnet list Flux.sln package --vulnerable --include-transitive`

## Execution Notes

- Keep each phase in a separate commit or review unit.
- Do not combine schema migrations with unrelated API or frontend changes.
- Prefer additive tests before behavior changes where a test harness exists.
- If a test harness is missing, document the gap in the implementation summary and use the narrowest manual verification.
- Do not edit generated `Flux.Web/wwwroot` as source; regenerate it from `flux-web-client`.
- Do not modify vendored `Libraries/TlsClient.NET` for these phases.
