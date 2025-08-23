# Firefox Profile Support Test

This document demonstrates the Firefox profile functionality that has been added to the PlaywrightOpenBrowser block.

## What was implemented:

1. **Added `firefoxProfilePath` parameter** to the `PlaywrightOpenBrowser` method in `RuriLib\Blocks\Playwright\Browser\Methods.cs`

2. **Modified browser launch logic** to use `LaunchPersistentContextAsync` when a Firefox profile is specified, which properly supports user data directories

3. **Updated fallback mechanisms** to preserve profile settings when falling back to built-in Firefox

## How to use:

1. **In the Block Stacker**: When you add a "Open Browser" block from the Playwright > Browser category, you'll now see a new parameter called "Firefox Profile Path"

2. **Set the profile path**: Enter the full path to your Firefox profile directory, for example:
   - `C:\Users\YourName\AppData\Roaming\Mozilla\Firefox\Profiles\your-profile-folder`
   - Or a custom profile directory you've created

3. **Browser Type**: Make sure to set the browser type to "Firefox" for the profile to be used

## Technical Details:

- When `firefoxProfilePath` is provided and browser type is Firefox, the system uses `playwright.Firefox.LaunchPersistentContextAsync()` instead of regular `LaunchAsync()`
- This creates a persistent browser context that maintains cookies, local storage, and other browser data
- The profile path is preserved in fallback scenarios when custom Firefox executables fail
- The browser context is stored as "playwrightContext" in the bot data for potential future use

## Benefits:

- **Profile Switching**: Easy switching between different Firefox profiles without changing global settings
- **Session Persistence**: Maintain cookies, login sessions, and browser state across runs
- **Per-Config Profiles**: Different configurations can use different profiles
- **Block-Level Control**: Profile is set at the block level, not in global RLSettings

## Example Usage:

```
Playwright Open Browser:
- Browser Type: Firefox
- Headless: false
- Extra Args: []
- Firefox Profile Path: C:\Users\user\MyFirefoxProfile
```

This will launch Firefox with the specified profile, maintaining all the profile's settings, extensions, and stored data.