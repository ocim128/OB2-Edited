# Development

Build, run, test, and validate Flux locally.

## Prerequisites

| Requirement | Detail |
|---|---|
| OS | Windows 10/11. `Flux.Native` is WPF (`net8.0-windows`), so the desktop app does not build or run elsewhere. |
| .NET SDK | **8.0.401**. `global.json` pins this exact SDK; newer SDKs installed side-by-side are not used while the pin is in place. |
| Git | The `Libraries/TlsClient.NET` directory is a nested git repository with a local patch. Do not re-clone or reset it. |
| `dotnet-ef` (optional) | Only needed to add or list EF Core migrations. |

Restore writes packages into the repository-local `packages/` folder because `nuget.config` overrides `globalPackagesFolder`. A clean checkout will therefore spend time repopulating `packages/`, and that folder can be large.

## Build And Test

From the repository root:

```powershell
dotnet restore Flux.sln
dotnet build Flux.sln -c Debug --no-restore
```

Targeted builds are much faster and avoid rebuilding the WPF app:

```powershell
dotnet build RuriLib/RuriLib.csproj -c Debug --no-restore
dotnet build Flux.Shared/Flux.Shared.csproj -c Debug --no-restore
```

Tests (xUnit, all `net8.0`):

```powershell
dotnet test RuriLib.Tests/RuriLib.Tests.csproj
dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj
dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj
```

`dotnet test Flux.sln` also works, but it builds `Flux.Native` as part of the solution; use the per-project commands for the inner loop.

## Run The Desktop App

The supported local loop is the publish-and-launch script:

```powershell
Flux.Native\test_build\run_testing.bat            # incremental
Flux.Native\test_build\run_testing.bat --clean    # force rebuild
Flux.Native\test_build\run_testing.bat --no-launch
```

`test_build\run_testing.bat` at the repository root is a thin wrapper that forwards arguments to the script above.

What the script does:

1. Kills running `Flux.Native.exe` processes.
2. Backs up `test_build/testing/UserData` and `test_build/testing/.playwright`.
3. Runs `dotnet publish Flux.Native -c Testing -r win-x64 --self-contained false` into `Flux.Native/test_build/testing`.
4. Copies `Flux.Native/UserData` into the output, then restores the backup over it.
5. Removes non-Windows runtime folders to shrink output.
6. Launches the app from the publish directory.

The `Testing` configuration is optimized but not single-file (`Release` is single-file), which is why the output directory is inspectable and incremental.

You can also run the app straight from the SDK:

```powershell
dotnet run --project Flux.Native/Flux.Native.csproj
```

The app always uses `UserData/` next to the executable. `dotnet run` therefore uses `Flux.Native/bin/.../UserData`, not `Flux.Native/UserData` — different data than the script. If the app refuses to start with a `UserData directory ... is not writable` error, that is the check in `Flux.Native/App.xaml.cs`, not a permissions bug in your shell.

## Test Layout

| Project | Covers | Notes |
|---|---|---|
| `RuriLib.Tests` | Debugger execution, HTTP transports, transpilers, config pack/save round-trips, parsing, Playwright block descriptors | `RuriLib` has `[assembly: InternalsVisibleTo("RuriLib.Tests")]`, so tests can use internal handlers and helpers |
| `RuriLib.Http.Tests` | `RLHttpClient`, `ProxyClientHandler`, zero-timeout regressions | Uses a loopback `TcpListener` (`LoopbackHttpServer.cs`) |
| `Flux.Shared.Tests` | Hit dedupe keys, recent-hits query shape | EF Core InMemory provider, no real database |

Conventions worth following when adding tests:

- No external network. `RuriLib.Tests/Infrastructure/TestHttpServer.cs` is a programmable loopback HTTP server; `TestWorkspace.cs` gives each test a throwaway directory under the temp folder.
- `HttpTransportTestHelper.cs` builds a `BotData` wired to a real `RuriLibSettingsService` and `BotLogger`; reuse it instead of hand-constructing providers.
- `HttpTransportParityTests` currently has two `[Theory(Skip = "HTTPS test server not implemented yet")]` cases. Skipped tests are known gaps, not passing coverage — do not report "all tests pass" without mentioning them.

## Database Migrations

`Flux.Core/ApplicationDbContextFactory.cs` is a design-time factory with a hardcoded `Data Source=UserData/Flux.db` connection string, so migrations do not need the desktop startup project:

```powershell
dotnet ef migrations add <Name> --project Flux.Core/Flux.Core.csproj
dotnet ef migrations list --project Flux.Core/Flux.Core.csproj
```

The app applies pending migrations on startup in a background task. A failed migration surfaces as a `Startup Error` alert ("Some background initialization failed: ...") and, in `Debug` builds, a `startup-trace.log` line — not as a build error.

## Validation Before Merging

Minimum for any change that touches runtime, persistence, or the desktop shell:

```powershell
dotnet build Flux.sln -c Debug
dotnet test RuriLib.Tests/RuriLib.Tests.csproj
dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj
dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj
```

Also run the vulnerability check when packages changed:

```powershell
dotnet list Flux.sln package --vulnerable --include-transitive
```

If you changed XAML or viewmodels, the automated tests will not catch it — publish with `run_testing.bat` and exercise the screen.

## Troubleshooting

| Symptom | Check |
|---|---|
| Startup hangs before the window appears | `startup-trace.log` next to the executable. It is only written in `Debug` builds (`#if DEBUG || OB2_TRACE`); `Testing` and `Release` builds define `TRACE` only, so the file will be absent. |
| Unhandled exception dialog | `crash.log` next to the executable, written by the `App` logger. |
| Config fails to load or compile with a cache-related error | Delete `UserData/CompiledScripts/`. The cache is hash-keyed on generated C# and assembly identity, so normal source edits do not leave stale entries — a bad entry means a corrupt DLL on disk. |
| Build warns about package downgrades or restore loops | Restore again from the root; `packages.lock.json` files are checked in and `RestorePackagesWithLockFile` is on. |
| Restore/build emits `NU1902` / `NU1903` | Known advisories on current pins (`AngleSharp` 1.1.2, `SSH.NET` 2024.0.0) surface on every build. They are not caused by your change; bumping them is a separate, tested change. |
| Build output is unexpectedly quiet about warnings | `Directory.Build.props` suppresses a fixed warning set and sets `TreatWarningsAsErrors=false`. Compiler warnings are not a gate. |
| Tests fail only on a machine with no proxy/network | Transport tests are loopback-only; a failure there usually means a socket bind issue, not connectivity. |
