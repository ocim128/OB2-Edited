# Playwright In This Repo

## Purpose
This document explains how Playwright is used in this codebase, where the relevant code lives, how launch and cleanup work, what the current stealth layer actually does, and why the package version must stay pinned.

## Hard Rule
Do not update `Microsoft.Playwright` in this repository.

Current pinned version:

- `RuriLib/RuriLib.csproj`: `1.41.0`
- `Flux.Native/Flux.Native.csproj`: `1.41.0`

This pin is intentional.

## Why The Version Must Stay Pinned
Newer Playwright releases changed behavior that this repo currently benefits from.

The two repo-level advantages of staying on `1.41.0` are:

1. Older bundled Chromium behavior.
This repo's Chromium path, stealth assumptions, extension loading, and runtime expectations were built around the older Playwright-managed Chromium behavior. Newer Playwright releases switched bundled Chromium to Chrome for Testing. That is not a neutral change here.

2. Pre-removal Chromium Manifest V2 extension compatibility.
This repo accepts arbitrary Chromium extension directories through `extensionPath` and loads them with:
`--disable-extensions-except=...`
`--load-extension=...`

The code does not enforce Manifest V3. Newer Playwright releases dropped Chromium Manifest V2 extension support. Staying on `1.41.0` preserves broader extension compatibility than newer releases.

Repo policy:

- Do not bump Playwright as a maintenance task.
- Do not treat "latest Playwright" as an improvement by default.
- If anyone wants a newer version, that is a separate migration project with full revalidation of extension loading, Firefox profile launch, stealth behavior, runtime installation, and target-site behavior.

## Where Playwright Lives

### Core block implementation
- `RuriLib/Blocks/Playwright/Browser/Methods.cs`
- `RuriLib/Blocks/Playwright/Browser/Methods.Helpers.cs`
- `RuriLib/Blocks/Playwright/Browser/Methods.Cleanup.cs`
- `RuriLib/Blocks/Playwright/Browser/Methods.Captcha.cs`
- `RuriLib/Blocks/Playwright/Page/Methods.cs`
- `RuriLib/Blocks/Playwright/Elements/Methods.cs`
- `RuriLib/Blocks/Playwright/Cookies/Methods.cs`
- `RuriLib/Blocks/Playwright/PlaywrightHelpers.cs`

### Runtime and configuration
- `RuriLib/Providers/Playwright/PlaywrightRuntimeService.cs`
- `RuriLib/Providers/Playwright/DefaultPlaywrightBrowserProvider.cs`
- `RuriLib/Providers/Playwright/IPlaywrightBrowserProvider.cs`
- `RuriLib/Helpers/PlaywrightLaunchConfigurator.cs`
- `RuriLib/Models/Settings/PlaywrightSettings.cs`
- `RuriLib/Models/Bots/BrowserSessionState.cs`
- `RuriLib/Models/Bots/BotData.cs`

### Native app integration
- `Flux.Native/ViewModels/Settings/RLSettingsViewModel.cs`
- `Flux.Native/Views/Pages/Settings/RLSettings.xaml.cs`
- `Flux.Native/Services/ZipProfileLauncher.cs`

### Registration
- `RuriLib/Helpers/Blocks/BuiltInBlockRegistry.cs`

## Settings Model
Playwright configuration starts from `PlaywrightSettings`.

Current settings:

- `BrowserType`
- `ChromiumBinaryLocation`
- `FirefoxBinaryLocation`
- `WebkitBinaryLocation`
- `Headless`
- `DrawMouseMovement`
- `TimeoutMilliseconds`
- `IgnoreHTTPSErrors`
- `ExtraArgs`

Important nuance:

- The settings model is broader than the current native settings UI.
- `RLSettingsViewModel` currently exposes only:
  - Chromium binary path
  - Firefox binary path
  - Webkit binary path
  - Draw mouse movement

So `Headless`, `TimeoutMilliseconds`, `IgnoreHTTPSErrors`, and `ExtraArgs` exist and are consumed by the runtime, but are not fully surfaced in the current native settings page.

Another nuance:

- `DrawMouseMovement` exists in the settings model and provider, but is not currently consumed by the Playwright block implementation itself.

## Session State
Per-bot Playwright state lives in `BotData.PlaywrightSession`.

Tracked objects:

- `IBrowser`
- `IBrowserContext`
- `IPage`
- `IFrame`
- `IPlaywright`
- cleanup state
- browser type and headless mode
- real browser process ids
- Firefox process ids
- temporary Firefox profile path
- temporary Chromium user-data path
- temporary artifact paths

This state is what all Playwright blocks read and mutate.

## Runtime Installation
`PlaywrightRuntimeService` prepares browser runtimes on demand.

Current behavior:

- Uses `%LOCALAPPDATA%\\ms-playwright` as the default user-local runtime path.
- Installs missing browser bundles through the Playwright CLI.
- Validates disk space and write access before install.
- Skips installation if a valid custom executable path is configured.

Packaging note:

- `Flux.Native.csproj` explicitly excludes `.playwright/**` from build output.
- Browser binaries are expected to be installed or resolved at runtime, not bundled with app output.

Native settings install note:

- `RLSettingsViewModel.InstallPlaywrightBrowsers(...)` currently installs only `Chromium` and `Firefox`.
- It does not install `Webkit`.

## Browser Opening Flow
The main entry point is `PlaywrightOpenBrowser`.

Open flow:

1. Read provider settings.
2. Normalize args and browser type.
3. Configure Chromium extension flags or Firefox profile and addon settings.
4. Resolve a custom executable path, or fall back to the Playwright-managed runtime.
5. Create `IPlaywright` through `PlaywrightRuntimeService`.
6. Launch one of:
   - Firefox persistent context
   - Chromium persistent context for extensions
   - regular browser
7. Create or pick a page.
8. For Chromium only:
   - inject context init-script stealth
   - apply page-level CDP stealth
9. Register cleanup state and tracked temp artifacts.

## Cleanup Flow
Cleanup is centralized in `Methods.Cleanup.cs` plus `BotData.DisposeTrackedBrowserSessions()`.

It handles:

- stopping manual close watchers
- disposing the `IPlaywright` instance
- killing tracked real browser processes when needed
- killing tracked Firefox processes
- cleaning temporary Firefox profiles
- cleaning temporary Chromium user-data directories
- cleaning tracked temp artifacts
- clearing session state

If the custom cleanup path is unavailable, `BotData` still falls back to closing the context or browser directly and then disposing the Playwright instance.

## Browser-Specific Behavior

### Chromium
Chromium has the most custom logic.

Important behavior:

- supports `extensionPath`
- uses a persistent context when loading extensions
- applies Chromium-only stealth init script
- applies Chromium-only CDP debugger suppression

Extension behavior:

- `ConfigureChromiumExtension(...)` adds:
  - `--disable-extensions-except=<path>`
  - `--load-extension=<path>`

- `LaunchChromiumWithExtensionAsync(...)` uses `LaunchPersistentContextAsync(...)` with a temporary user-data directory.

Why the version pin matters:

- This code accepts arbitrary extension directories.
- It does not restrict callers to Manifest V3-only inputs.
- Updating Playwright risks breaking extension compatibility directly.

### Firefox
Firefox has the most operational customization.

Important behavior:

- optional explicit Firefox profile path
- automatic temporary profile creation for visible mode
- optional Firefox addon installation from `.xpi`
- prefs forced to allow unsigned extensions
- fallback from custom Firefox executable to Playwright-managed Firefox
- manual close watcher support in visible mode

Addon behavior:

- Addons are copied into the profile `extensions/` directory.
- The code tries to extract addon ids from `manifest.json`.
- Both `browser_specific_settings.gecko.id` and legacy `applications.gecko.id` are recognized.

Profile behavior:

- If a Firefox addon path is provided without a profile, a temporary profile is created.
- If Firefox runs visible and no profile is provided, a dedicated temporary visible-mode profile is created.

### WebKit
WebKit exists in the type system and launch model, but is the least integrated path.

Important limitations:

- The native browser-install flow does not install WebKit.
- WebKit depends more heavily on either a preinstalled Playwright runtime or a custom executable path.

## Public Block Surface

### Browser blocks
- Open Browser
- Close Browser
- New Page
- Close Page
- Get Pages
- Switch to Page
- Solve CAPTCHA

### Page blocks
- Navigate To
- Reload
- Go Back
- Go Forward
- Get URL
- Get Title
- Get Source
- Screenshot
- Wait
- Wait for Load
- Wait for Network Idle
- Execute JS
- Scroll to Top
- Scroll to Bottom
- Set Viewport
- Set User Agent
- Switch to Main Frame

### Element blocks
- Click Element
- Type Text
- Fill Element
- Clear Element
- Get Text
- Get Inner Text
- Get Inner HTML
- Get Attribute Value
- Set Attribute
- Element Exists
- Wait For Element
- Hover Element
- Double Click
- Right Click
- Select Option
- Check Element
- Uncheck Element
- Focus Element
- Press Key
- Switch to Frame

### Cookie blocks
- Get Cookie
- Set Cookie
- Delete Cookie
- Clear All Cookies
- Load Cookies
- Export Cookies
- Cookie Exists
- Set Multiple Cookies
- Get Cookies

## Stealth Layer
Chromium stealth is implemented in two layers.

### 1. Context init script
Applied through `AddInitScriptAsync(...)`.

Current areas patched:

- `navigator.webdriver`
- `navigator.permissions.query`
- `window.chrome.runtime`
- `navigator.plugins`
- `navigator.languages`
- `window.outerWidth`
- `window.outerHeight`
- console method wrappers
- `performance.now`
- `Function.prototype.toString`
- deletion of some CDP-related globals

### 2. Page-level CDP stealth
Applied per page with a CDP session.

Current purpose:

- enable the Debugger domain
- skip all pauses so debugger statements do not stop execution

Important limitation:

- The fallback `New Page` path that creates a fresh context reapplies CDP stealth, but does not currently reapply the Chromium init script to that new context.

## Current Known Limitations
These are real current constraints in this repo.

1. `navigator.plugins` is obviously fake.
The current patch returns `[1, 2, 3, 4, 5]`.

2. Wrapped console functions do not look native.
The `Function.prototype.toString` shim only protects itself, not wrapped console methods.

3. `performance.now` patch is effectively dead code.
It calls the original implementation unchanged.

4. Window metrics are unrealistic.
`outerWidth` is forced to `innerWidth`, and `outerHeight` is `innerHeight + screenY`.

5. New fallback context misses the init-script stealth.
The page still gets CDP stealth, but not the context init script.

6. `window.chrome.runtime` is too sparse.
It is just `{}`.

7. "Set User Agent" only changes request headers.
The current implementation uses `SetExtraHTTPHeadersAsync(...)`. It does not recreate the context with a matching Playwright `UserAgent` option, so HTTP-level UA and JS-visible UA can drift.

## Native App Integration

### RL Settings
Native settings integrate with Playwright in two ways:

1. Editable Playwright settings through `RLSettingsViewModel`
2. Browser installation through `InstallPlaywrightBrowsers(...)`

### ZIP profile launcher
`Flux.Native/Services/ZipProfileLauncher.cs` is a separate Firefox utility path.

It:

- extracts a selected folder from a ZIP archive
- launches Firefox as a persistent context against that extracted profile
- reuses `PlaywrightSettings`
- applies Firefox-safe defaults through `PlaywrightLaunchConfigurator`

This is not the same as the normal Playwright block execution path.

Important nuance:

- It uses Playwright directly.
- It does not reuse the Chromium stealth init-script path because it is Firefox-only.

## Launch Configuration Notes
`PlaywrightLaunchConfigurator` centralizes launch tweaks.

Current behavior:

- ensures sandbox-relaxing flags for Chromium
- adds Chromium stealth-related flags
- applies Firefox-safe defaults by disabling GPU-heavy or acceleration-heavy prefs
- strips Chromium-only sandbox flags from Firefox launches

This helper is shared between the normal Playwright launch path and the ZIP profile launcher.

## What To Avoid

- Do not bump `Microsoft.Playwright`.
- Do not assume upstream Playwright changes are improvements for this repo.
- Do not narrow Chromium extension loading to Manifest V3-only unless that is an intentional product decision.
- Do not change runtime installation behavior without rechecking:
  - custom executable fallback
  - Firefox profile launch
  - extension loading
  - temp artifact cleanup
  - native settings install flow

## If Someone Still Wants To Change The Version
That is not a normal maintenance task.

Minimum migration scope would include:

1. Revalidating Chromium extension loading end to end.
2. Revalidating Firefox profile launch and addon installation.
3. Revalidating all stealth patches against real target sites.
4. Revalidating runtime installation and packaged deployment behavior.
5. Revalidating assumptions tied to old bundled Chromium behavior.

Until that full migration happens, the correct repo policy is:

- keep `Microsoft.Playwright` at `1.41.0`
- document newer-version proposals as migration ideas, not routine upgrades

## References

### Local code
- `RuriLib/Blocks/Playwright/`
- `RuriLib/Providers/Playwright/`
- `RuriLib/Helpers/PlaywrightLaunchConfigurator.cs`
- `RuriLib/Models/Settings/PlaywrightSettings.cs`
- `RuriLib/Models/Bots/BrowserSessionState.cs`
- `Flux.Native/Services/ZipProfileLauncher.cs`

### External rationale for the version pin
- Playwright .NET releases:
  - `v1.57` switched bundled Chromium to Chrome for Testing
  - `v1.55` dropped Chromium Manifest V2 extension support

Those upstream changes are exactly why this repo should not treat a Playwright update as a routine improvement.
