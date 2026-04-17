# Low-Risk Efficiency Audit — OB2-Edited

> Scope: All projects except Flux.Web / flux-web-client
> Date: 2025-04-17
> Methodology: Static analysis of 1039 C# files across RuriLib, Flux.Core, Flux.Shared, Flux.Native, RuriLib.Http, RuriLib.Proxies, RuriLib.Parallelization

---

## Priority Tier 1 — HIGH impact, VERY LOW risk

These target hot paths (per-data-line, per-bot-execution, per-HTTP-request) and fix genuine waste.

### 1. Repeated Regex compilation in DataLine.IsValid (per-data-line hot path)
- **File:** `RuriLib/Models/Data/DataLine.cs:23`
- **Problem:** `Regex.Match(Data, Type.Regex)` recompiles the regex on every call for every data line processed. With millions of lines this is massive overhead.
- **Fix:** Cache compiled Regex on the WordlistType: `_compiledRegex ??= new(Regex, RegexOptions.Compiled)`, use `CompiledRegex.IsMatch(Data)`.
- **Risk:** Very low — same behavior, cached compilation.

### 2. Conditions.cs — Regex compilation per comparison
- **File:** `RuriLib/Functions/Conditions/Conditions.cs:32-33`
- **Problem:** `Regex.Match(leftTerm, rightTerm)` recompiles user-provided pattern every evaluation, inside conditions that run in loops.
- **Fix:** Use existing `RegexCache.GetOrCreate(rightTerm).IsMatch(leftTerm)`.
- **Risk:** Very low — RegexCache is already thread-safe in this codebase.

### 3. LineParser.cs — 6 static-pattern regex compilations per parse call
- **File:** `RuriLib/Helpers/LoliCode/LineParser.cs:21,39,57,80,98,213`
- **Problem:** Six different `Regex.Match(input, "static-pattern")` calls, each recompiling on every invocation during LoliCode transpilation.
- **Fix:** Use `[GeneratedRegex]` (already used elsewhere in this project) or static cached fields.
- **Risk:** Very low.

### 4. HexConverter.ToHexString — LINQ allocation per byte
- **File:** `RuriLib/Functions/Conversion/HexConverter.cs:33`
- **Problem:** `string.Concat(bytes.Select(b => Convert.ToString(b, 16).PadLeft(2, '0')))` allocates iterator + string per byte.
- **Fix:** Replace with `Convert.ToHexString(bytes).ToLowerInvariant()` (.NET 6+ built-in).
- **Risk:** Very low — identical output.

### 5. Wordlist constructor — double-assignment bug + blocking line count
- **File:** `RuriLib/Models/Data/Wordlist.cs:41`
- **Problem:** `Total = countLines ? Total = File.ReadLines(path).Count() : 0` — double assignment (`Total = Total = ...`) and synchronous blocking enumeration of entire file.
- **Fix:** `Total = countLines ? File.ReadLines(path).Count() : 0` (remove double assignment). Ideally count in background.
- **Risk:** Very low — removes a bug.

### 6. DashboardService — loads entire tables to count rows
- **File:** `Flux.Shared/Services/DashboardService.cs:131-151`
- **Problem:**
  - `CountConfigsAsync()`: unpacks ALL .opk config files from disk just to count them.
  - `CountProxiesAsync()`: loads ALL proxy groups + ALL their proxies into memory just to sum counts.
  - `CountWordlistsAsync()`: loads ALL wordlist entities to count + sum totals.
- **Fix:**
  - Configs: count files on disk instead of deserializing.
  - Proxies: `_proxyRepository.GetAll().CountAsync()`.
  - Wordlists: `CountAsync()` + `SumAsync(w => w.Total)`.
- **Risk:** Very low — same results, server-side aggregation.

### 7. Missing AsNoTracking on 6+ read-only EF Core queries
- **Files:** `DashboardService.cs`, `JobProjectionService.cs`, `ProxyReloadService.cs`, `JobManagerService.cs`
- **Problem:** Entities loaded for display/mapping only are tracked by EF Core change tracker unnecessarily.
- **Fix:** Add `.AsNoTracking()` to all read-only queries.
- **Risk:** Very low — entities are never modified through these paths.

### 8. HybridWordlistRepository — re-reads file from disk to count lines after writing from MemoryStream
- **File:** `Flux.Core/Repositories/HybridWordlistRepository.cs:69-74`
- **Problem:** Writes MemoryStream to disk, then re-reads entire file via `File.ReadLines().Count()` to count lines. Data was already in memory.
- **Fix:** Count lines from the MemoryStream buffer before writing to disk.
- **Risk:** Very low — eliminates an entire file read I/O.

---

## Priority Tier 2 — MEDIUM impact, VERY LOW risk

### 9. new Random() in 4+ hot paths (seed collision + allocation)
- **Files:**
  - `RuriLib/Functions/Crypto/Crypto.cs:353`
  - `RuriLib/Functions/Http/HttpRequestNormalizer.cs:180`
  - `RuriLib/Helpers/VariableNames.cs:53`
  - `RuriLib/Extensions/IListExtensions.cs:15`
- **Problem:** `new Random()` per call risks seed collision in concurrent scenarios and allocates unnecessarily.
- **Fix:** Replace with `Random.Shared` (available in .NET 6+).
- **Risk:** Very low.

### 10. StringExtensions.Unescape — 3 inline Regex.Replace per call
- **File:** `RuriLib/Extensions/StringExtensions.cs:44-46`
- **Problem:** Three `Regex.Replace` calls with static patterns recompiled on each invocation.
- **Fix:** Use `[GeneratedRegex]` or existing `RegexCache`.
- **Risk:** Very low.

### 11. StringExtensions.CountLines — allocates full string array to count
- **File:** `RuriLib/Extensions/StringExtensions.cs:222-223`
- **Problem:** `input.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None).Length` allocates a full array just to count.
- **Fix:** Iterate and count `\n` characters, zero allocations.
- **Risk:** Very low.

### 12. VariableNames.RandomName — new Random() + LINQ overhead
- **File:** `RuriLib/Helpers/VariableNames.cs:51-57`
- **Problem:** `new Random()` + `Enumerable.Repeat().Select().ToArray()` per call.
- **Fix:** Use `Random.Shared` + `string.Create()` with span-based fill.
- **Risk:** Very low.

### 13. BinaryConverter.ToBinaryString — LINQ allocation per byte
- **File:** `RuriLib/Functions/Conversion/BinaryConverter.cs:30`
- **Problem:** Same pattern as HexConverter — allocates per byte.
- **Fix:** Use `string.Create()` with span-based approach.
- **Risk:** Very low.

### 14. HttpRequestNormalizer.GenerateMultipartBoundary — overengineered
- **File:** `RuriLib/Functions/Http/HttpRequestNormalizer.cs:177-188`
- **Problem:** `new Random()` + `Math.Floor` + `Convert` + `StringBuilder` for 16 chars, then `.ToLower()`.
- **Fix:** Use `string.Create()` + `Random.Shared`.
- **Risk:** Very low.

### 15. Duplicate CRLF byte[] allocation in HttpResponseBuilder
- **File:** `RuriLib.Http/HttpResponseBuilder.cs:27,42`
- **Problem:** Instance `CRLF` field + static `CrLf` field hold identical `"\r\n"` bytes. Instance one allocates per builder.
- **Fix:** Remove instance field, use static everywhere.
- **Risk:** Very low.

### 16. HttpClient created per-request in ConfigService
- **File:** `Flux.Core/Services/ConfigService.cs:144`
- **Problem:** `using HttpClient client = new()` inside lambda per remote endpoint. Causes socket exhaustion under load.
- **Fix:** Use static/shared `HttpClient` or `IHttpClientFactory`.
- **Risk:** Low — ensure headers are cleared between requests.

### 17. Repeated JsonSerializerSettings allocation in JobManagerService
- **File:** `Flux.Core/Services/JobManagerService.cs:54,243`
- **Problem:** Identical `new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto }` in two methods.
- **Fix:** Extract to `private static readonly JsonSerializerSettings`.
- **Risk:** Very low — settings are thread-safe for read usage.

### 18. FluxSettingsService — SaveAsync().Wait() in constructor
- **File:** `Flux.Core/Services/FluxSettingsService.cs:48`
- **Problem:** `SaveAsync().Wait()` blocks synchronously, can deadlock in UI contexts.
- **Fix:** Use synchronous `File.WriteAllText()` directly.
- **Risk:** Very low — same result, no async state machine needed.

---

## Priority Tier 3 — WPF-specific, MEDIUM impact, LOW risk

### 19. Dispatcher.Invoke where BeginInvoke suffices
- **Files:** `Flux.Native/ViewModels/Job/MultiRunJobViewerViewModel.Refresh.cs:141`, `JobsViewModel.cs:157`, `Services/HotkeyService.cs:424-446`
- **Problem:** `RunOnUiThread()` uses synchronous `Dispatcher.Invoke`, blocking timer thread.
- **Fix:** Change to `Dispatcher.BeginInvoke` — no return value needed.
- **Risk:** Very low — fire-and-forget updates.

### 20. SolidColorBrush allocated on every property access (~20/sec)
- **Files:** `JobRowViewModels.cs:58-69`, `HotkeyService.cs:697-707`, `DebuggerUIManager.cs` lines 77,83,132,138,196,202,356,362
- **Problem:** `new SolidColorBrush(...)` on every binding evaluation for status colors. These are computed properties called per-tick.
- **Fix:** Cache brushes as `static readonly` + `.Freeze()` for thread safety.
- **Risk:** Very low — colors don't change between evaluations of the same value.

### 21. ObservableCollection wholesale replacement every refresh tick
- **Files:** `MultiRunJobViewerViewModel.Refresh.cs:72,130`, `JobsViewModel.cs:127`
- **Problem:** `new ObservableCollection<T>(...)` every 1-2s causes WPF to tear down and rebuild ListView containers. Creates GC pressure, loses scroll state.
- **Fix:** Reuse existing collection with `.Clear()` + `.AddRange()`, or implement `RangeObservableCollection`.
- **Risk:** Low — same visual result, smoother updates.

### 22. INotifyPropertyChanged fired for unchanged values (5 ViewModels)
- **Files:** `ConfigsViewModel.cs:41-49`, `ProxiesViewModel.cs:68-78`, `HitsViewModel.cs:37-47`, `WordlistsViewModel.cs:34-43`
- **Problem:** SearchString setters don't check equality before firing `PropertyChanged` + `CollectionView.Refresh()`.
- **Fix:** Add `if (value == searchString) return;` guard.
- **Risk:** Very low — same behavior, fewer unnecessary UI refreshes.

### 23. Parallelizer.Status setter fires event without change-check
- **File:** `RuriLib.Parallelization/Parallelizer.cs:31-39`
- **Problem:** Status setter always calls `OnStatusChanged` even when unchanged. Subscribers cascade 9+ `OnPropertyChanged` calls.
- **Fix:** Add `if (status == value) return;` before assignment.
- **Risk:** Very low.

### 24. JobsViewModel — Timer + SemaphoreSlim never disposed
- **File:** `Flux.Native/ViewModels/Job/JobsViewModel.cs:22-23,60`
- **Problem:** Holds `System.Threading.Timer` (2s interval) + `SemaphoreSlim` but never disposes.
- **Fix:** Implement `IDisposable`, dispose both.
- **Risk:** Very low — cleanup-only.

---

## Priority Tier 4 — ConfigureAwait(false) sweep (MEDIUM impact per-library, VERY LOW risk)

All library code (RuriLib, Flux.Core, Flux.Shared) should use `ConfigureAwait(false)` on every `await`. This prevents unnecessary SynchronizationContext captures and potential deadlocks. Files with the most missing calls:

| File | Missing count | Hot path? |
|------|--------------|-----------|
| `RuriLib/Models/Jobs/Execution/BotExecutionCoordinator.cs` | ~6 | YES — per-bot |
| `RuriLib/Helpers/PauseTokenSource.cs` | ~10 | YES — per-step |
| `RuriLib/Models/Jobs/ProxyCheckJob.cs` | 3 | YES — per-proxy |
| `RuriLib/Blocks/Selenium/*/Methods.cs` | ~5 | Per-block |
| `RuriLib/Blocks/Puppeteer/*/Methods.cs` | ~30+ | Per-block |
| `RuriLib/Blocks/Android/*/Methods.cs` | ~10+ | Per-block |

This is a mechanical sweep — low risk, high coverage.

---

## Summary Statistics

| Category | Findings | Impact |
|----------|----------|--------|
| Regex compilation waste | 3 | HIGH |
| EF Core query waste | 3 | HIGH |
| Allocation waste (LINQ, arrays, new()) | 6 | MEDIUM |
| HttpClient / I/O waste | 3 | MEDIUM |
| WPF UI waste | 4 | MEDIUM |
| ConfigureAwait(false) | ~64 missing | MEDIUM |
| Missing cancellation tokens | 4 | LOW |
| Event/IDisposable leaks | 2 | LOW |
| **Total** | **~89 issues** | |

### Top 5 quick wins (biggest bang for least effort):
1. **Finding 4** — `Convert.ToHexString` (1-line change, huge per-byte allocation savings)
2. **Finding 5** — Fix Wordlist double-assignment (1-line change, removes a bug)
3. **Finding 6** — DashboardService server-side counting (3 methods, eliminates loading entire tables)
4. **Finding 2** — Conditions.cs RegexCache (2-line change, cached regex for all condition evaluations)
5. **Finding 23** — Parallelizer.Status guard (1-line change, eliminates cascading event spam)
