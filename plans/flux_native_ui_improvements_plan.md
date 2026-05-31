# Flux.Native UI Improvements Plan

## Scope

This plan covers the low-risk Flux.Native UI/viewmodel improvements identified in the static analysis:

1. Fix the Tools page cache/disposal mismatch.
2. Dispose transient job viewer pages/viewmodels.
3. Reuse job and bot row viewmodels instead of replacing rows.
4. Debounce search filters on large UI lists.
5. Use no-tracking reads for display-oriented UI list loads.
6. Cache decoded config icon bitmaps.
7. Marshal job viewer property updates onto the UI thread.
8. Centralize repeated ListView/GridView styles.

No implementation starts until this plan is reviewed.

## Assumptions And Unknowns

- `Flux.Native` is a WPF desktop application using cached navigation pages through `NavigationService`.
- Main UI pages are created by `IPageFactory` / `PageFactory`.
- Most long-lived page viewmodels are registered as singletons in `App.xaml.cs`; `HomeViewModel` and editor subpage viewmodels are transient.
- Job viewer pages are transient and managed by `NavigationHandler`, not by `NavigationService` page cache.
- Entity reads for hits, proxies, and wordlists use repositories from `Flux.Core` over EF Core/SQLite.
- No Flux.Native-specific automated test project was found. Validation will rely on targeted build plus manual UI regression checks unless a test project is added later.
- Unknown: whether Tools page state is expected to persist across navigation. The safer fix is to align caching behavior with existing `Monitor.Unloaded` cleanup semantics instead of preserving disposed state.

## Current Architecture Notes

### Module Boundaries

- `Flux.Native/Views/`
  WPF pages, dialogs, and controls.
- `Flux.Native/ViewModels/`
  UI state and commands for pages/tools/jobs/configs/data views.
- `Flux.Native/Services/NavigationService.cs`
  Caches main navigation pages.
- `Flux.Native/Services/Navigation/NavigationHandler.cs`
  Handles menu navigation plus transient job viewer pages.
- `Flux.Shared/`
  Job query/command/orchestration contracts used by native job UI.
- `Flux.Core/`
  EF repositories and persistence services used by data pages.

### Data Flow

- Data pages: WPF page -> `Flux.Native.ViewModels.Data.*ViewModel` -> `Flux.Core.Repositories`.
- Job list: `JobsViewModel` -> `IJobQueries.GetDesktopJobsAsync()` -> row viewmodels.
- Multi-run job viewer: timer -> `IJobQueries.GetMultiRunJobViewerSnapshotAsync()` -> `MultiRunJobViewerViewModel` -> WPF bindings.
- Proxy-check job viewer: job events/timer -> `ProxyCheckJobViewerViewModel` -> WPF bindings.
- Tools page: `Monitor` owns a `ToolsPageViewModel` composed of tool-specific viewmodels.

## Phase 1: Navigation And Lifetime Safety

### Objective

Remove page/viewmodel lifetime bugs before optimizing UI refresh paths.

### Scope

- Tools page caching/disposal.
- Transient job viewer disposal.
- No changes to job runtime behavior.

### Technical Tasks

- In `NavigationService`, exclude `MainWindowPage.Tools` from `_pageCache`.
- Keep `Monitor_Unloaded` cleanup behavior unchanged so Tools page disposal remains final for each Tools page instance.
- Add a disposal path for transient job viewer pages:
  - make `MultiRunJobViewer` and `ProxyCheckJobViewer` implement `IDisposable`;
  - unsubscribe viewmodel events before disposing the current viewmodel;
  - clear `DataContext`;
  - set local `vm` fields to null after cleanup.
- Update `NavigationHandler` to dispose the previous transient page:
  - before replacing `_transientPage` in `ChangePage`;
  - before clearing `_transientPage` in `OnNavigationServiceNavigated`.

### Dependencies

- Existing `Dispose()` methods in `MultiRunJobViewerViewModel` and `ProxyCheckJobViewerViewModel`.
- Existing `Monitor.CleanupAsync()` semantics.

### Risks/Blockers

- If Tools state is intentionally expected to persist across navigation, making Tools non-cached will reset tool inputs. This is preferable to reusing disposed tool state unless product behavior says otherwise.
- Disposal must not run while a page is still active in `MainFrame`.

### Deliverables

- Deterministic cleanup for transient job pages.
- Tools page no longer reuses disposed viewmodels.

### Validation/Testing Criteria

- Build `Flux.Native`.
- Navigate Tools -> another page -> Tools; OTP and other tools still function.
- Open several job viewers, navigate away, and verify no stale job viewer timers/log callbacks continue.
- Confirm active jobs continue running after closing their viewer page.

### Exit Criteria

- No cached page reuses disposed tool viewmodels.
- No transient job viewer remains subscribed after navigation away.

## Phase 2: Job Viewer State Updates

### Objective

Reduce UI churn and make job viewer updates thread-safe.

### Scope

- `JobsViewModel`
- `MultiRunJobViewerViewModel`
- `ProxyCheckJobViewerViewModel`
- job/bot row viewmodels

### Technical Tasks

- Replace identity-based collection sync in `JobsViewModel` with keyed sync by job ID and job type.
- Reuse existing `JobViewModel.ApplySnapshot(...)` for unchanged job rows, with snapshot application on the WPF dispatcher.
- Add mutable snapshot/update support to `BotViewModel`, with property notifications raised on the WPF dispatcher.
- Replace bot row replacement with keyed update by bot ID.
- Marshal snapshot application and `OnPropertyChanged` work to the WPF dispatcher in:
  - `MultiRunJobViewerViewModel.RefreshAsync`;
  - `ProxyCheckJobViewerViewModel` timer and job-event handlers.
- Keep DTO fetches off the UI thread.

### Dependencies

- `JobViewModel.ApplySnapshot(...)` already exists.
- `MultiRunJobViewerViewModel.RunOnUiThread(...)` already exists for collection updates.

### Risks/Blockers

- Sorting/selection behavior can change if keyed sync preserves existing row instances. This is usually desirable but must be checked.
- Bot rows need explicit property notifications after snapshot replacement.

### Deliverables

- Job list rows update in place.
- Bot rows update in place.
- Job viewer property updates are applied on the UI thread.

### Validation/Testing Criteria

- Start a multi-run job and observe job list stats updating without row flicker.
- Select a job row while refreshes happen; selection should not be lost due to row replacement.
- Open multi-run viewer; bot rows and result tabs update correctly.
- Open proxy-check viewer; progress/status updates correctly.
- No cross-thread binding exceptions during active jobs.

### Exit Criteria

- Collection mutations occur only for add/remove/reorder, not for every refresh.
- All WPF-bound job viewer state changes happen on the UI thread.

## Phase 3: Search And List Data Performance

### Objective

Reduce avoidable UI stalls in high-volume list pages.

### Scope

- Search boxes in data/config/job views.
- UI list queries that load display data.

### Technical Tasks

- Add `Delay=200` to search text bindings that currently use immediate `UpdateSourceTrigger=PropertyChanged`:
  - hits;
  - proxies;
  - wordlists;
  - configs;
  - jobs;
  - multi-run results.
- Keep ComboBox filters immediate.
- Add `.AsNoTracking()` to display-oriented list loads:
  - hits list;
  - proxies list and proxy groups where safe;
  - wordlists list.
- Verify delete/update paths still work with detached entities.
- Do not introduce paging in this phase; it is larger than the current low-risk scope.

### Dependencies

- Existing EF Core package references.
- Existing WPF binding support for `Delay`.

### Risks/Blockers

- Detached entities may expose repository assumptions in delete/update paths. If a path fails, either keep tracking for that specific path or delete by ID in a follow-up.
- Search debounce slightly delays visible filtering by 200 ms.

### Deliverables

- Debounced search filtering across large lists.
- Reduced EF tracking pressure for read-heavy UI pages.

### Validation/Testing Criteria

- Build `Flux.Native`.
- Type quickly in hit/proxy/wordlist/config/job search boxes; filtering should update after a short delay.
- Delete selected hits/proxies/wordlists after no-tracking reads.
- Change proxy group filters and verify counts update correctly.

### Exit Criteria

- Search behavior remains functionally equivalent.
- Read-heavy pages no longer track unnecessary entities unless required by a specific mutation flow.

## Phase 4: Config Icon Allocation Reduction

### Objective

Avoid repeated base64 decoding and bitmap allocation for config icons.

### Scope

- `ConfigViewModel.Icon`
- config list and select-config dialog icon bindings
- config metadata icon refresh path

### Technical Tasks

- Cache decoded `BitmapImage` in `ConfigViewModel` together with the source base64 string.
- Re-decode only when the source base64 string changes.
- Add empty/invalid base64 guard in `Images.Base64ToBitmapImage` caller or helper.
- Review `ConfigMetadataViewModel.Icon` for the same repeated decode pattern and cache where appropriate.

### Dependencies

- Existing `Images.Base64ToBitmapImage(...)` returns frozen `BitmapImage`.

### Risks/Blockers

- Invalid base64 should not break config list or select-config dialog rendering.

### Deliverables

- Config list and select-config dialog reuse decoded icon instances.
- Safer icon handling for missing/invalid image data.

### Validation/Testing Criteria

- Open Configs page with multiple configs; icons render.
- Open Select Config dialog; icons render.
- Change a config icon in metadata page; list/dialog show updated icon after refresh/navigation.

### Exit Criteria

- Icon bindings no longer decode base64 on every getter call.

## Phase 5: Shared ListView Style Consolidation

### Objective

Remove duplicated ListView/GridView XAML without changing behavior.

### Scope

- Repeated list styles in:
  - hits;
  - proxies;
  - wordlists;
  - configs;
  - select dialogs;
  - job viewer lists where compatible.

### Technical Tasks

- Create a shared XAML resource file under `Flux.Native/Styles/`.
- Move common `GridViewColumnHeader`, gripper, `ListViewItem`, and virtualized list style definitions into keyed styles.
- Merge the resource dictionary in `App.xaml` or existing style aggregation.
- Convert pages incrementally to reference shared styles.
- Leave page-specific context menus, handlers, and column definitions local.

### Dependencies

- Existing merged style dictionaries in `Flux.Native/Styles/`.
- Existing page-specific event handlers must remain attached.

### Risks/Blockers

- XAML resource lookup failures can break page load. Convert one page first, build, then continue.
- Some pages have small visual differences that should stay local unless identical.

### Deliverables

- Shared list style resource.
- Removed duplicated style blocks from converted pages.

### Validation/Testing Criteria

- Build `Flux.Native`.
- Open each converted list page.
- Verify selection, right-click context menus, sorting headers, virtualization, and scroll behavior.

### Exit Criteria

- At least the data list pages use the shared style without visual or behavioral regressions.

## Phase 6: Final Validation And Rollback Readiness

### Objective

Validate the combined changes and keep rollback simple.

### Scope

- Build validation.
- Manual UI regression pass.
- Git-level rollback boundaries.

### Technical Tasks

- Run targeted build:
  - `dotnet build Flux.Native/Flux.Native.csproj -c Debug --no-restore`
- If restore is required, run:
  - `dotnet build Flux.Native/Flux.Native.csproj -c Debug`
- Manual smoke checks:
  - navigation: Home, Tools, Jobs, job viewers, data pages, configs;
  - active job updates;
  - search/filter behavior;
  - delete flows after no-tracking reads;
  - config icon update/display.
- Keep commits/patches phase-scoped where possible.

### Dependencies

- Local .NET SDK compatible with `net8.0-windows`.
- Existing solution restore state.

### Risks/Blockers

- Native WPF behavior cannot be fully validated by build alone.
- No dedicated Flux.Native automated tests were found.

### Deliverables

- Build result.
- Manual validation notes.
- Any deferred issues explicitly listed.

### Validation/Testing Criteria

- Build passes.
- Manual smoke pass completes without navigation, binding, disposal, or list behavior regressions.

### Exit Criteria

- All implemented phases meet their exit criteria.
- Any skipped validation is explicitly reported.

## Performance Considerations

- Lifecycle fixes prevent background timer leaks before measuring other UI improvements.
- Keyed row updates should reduce UI collection churn on periodic refreshes.
- Debounced search prevents repeated full-list filter passes during typing.
- No-tracking reads reduce EF change tracker memory pressure on display-heavy pages.
- Icon caching reduces repeated allocations during list rendering.

## Failure Handling

- If Tools non-caching causes unacceptable state loss, switch to cached Tools with `Suspend`/`Resume` semantics instead of `Dispose` on `Unloaded`.
- If no-tracking breaks a mutation path, revert no-tracking only for that specific page or change the mutation to operate by ID.
- If shared ListView styles cause resource lookup issues, revert the affected page to local styles and keep the shared resource for already-validated pages.

## Rollback Strategy

- Roll back by phase, not as one large revert.
- Phase 1 and Phase 2 are behavioral safety changes and should be isolated from XAML style cleanup.
- Phase 5 should be last because XAML style consolidation is easiest to revert independently.

## Plan Review Adjustments

- Lifecycle fixes are first because they remove timer/event leaks that would obscure later performance validation.
- List style consolidation is last because it has more visual regression surface and less direct runtime impact.
- Database schema, API/contracts, deployment, and infrastructure sections are intentionally omitted because these changes do not touch those areas.
