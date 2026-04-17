using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Interactions;
using RuriLib.Attributes;
using RuriLib.Helpers.Android;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Android.Actions
{
    [BlockCategory("Android Actions", "Blocks for Android device actions and gestures", "#CDDC39")]
    public static class Methods
    {
        /// <summary>
        /// Takes a screenshot of the current screen.
        /// </summary>
        [Block("Takes a screenshot of the Android screen", name = "Android Screenshot")]
        public static async Task<byte[]> AndroidScreenshot(BotData data, string savePath = "")
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log("Taking screenshot...", LogColors.LimeGreen);

            var screenshotBytes = await Task.Run(() =>
            {
                var screenshot = driver.GetScreenshot();
                return screenshot.AsByteArray;
            }).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(savePath))
            {
                await File.WriteAllBytesAsync(savePath, screenshotBytes).ConfigureAwait(false);
                data.Logger.Log($"Screenshot saved to: {savePath}", LogColors.LimeGreen);
            }
            else
            {
                data.Logger.Log($"Screenshot captured ({screenshotBytes.Length} bytes)", LogColors.LimeGreen);
            }

            return screenshotBytes;
        }

        /// <summary>
        /// Presses a hardware key.
        /// </summary>
        [Block("Presses an Android hardware key", name = "Android Press Key")]
        public static async Task AndroidPressKey(BotData data, AndroidKeyCode keyCode)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Pressing key: {keyCode} ({(int)keyCode})", LogColors.LimeGreen);

            await Task.Run(() => driver.PressKeyCode((int)keyCode)).ConfigureAwait(false);

            data.Logger.Log("Key pressed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Taps at specific coordinates.
        /// </summary>
        [Block("Taps at specific coordinates on the screen", name = "Android Tap")]
        public static async Task AndroidTap(BotData data, int x, int y)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Tapping at coordinates: ({x}, {y})", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var finger = new PointerInputDevice(PointerKind.Touch, "finger");
                var actions = new ActionSequence(finger);

                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, y, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(MouseButton.Left));
                actions.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(100)));
                actions.AddAction(finger.CreatePointerUp(MouseButton.Left));

                driver.PerformActions(new List<ActionSequence> { actions });
            }).ConfigureAwait(false);

            data.Logger.Log("Tap completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Swipes on the screen.
        /// </summary>
        [Block("Swipes on the Android screen", name = "Android Swipe")]
        public static async Task AndroidSwipe(BotData data,
            int startX, int startY,
            int endX, int endY,
            int durationMs = 500)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Swiping from ({startX}, {startY}) to ({endX}, {endY})", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var finger = new PointerInputDevice(PointerKind.Touch, "finger");
                var actions = new ActionSequence(finger);

                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, startX, startY, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(MouseButton.Left));
                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, endX, endY, TimeSpan.FromMilliseconds(durationMs)));
                actions.AddAction(finger.CreatePointerUp(MouseButton.Left));

                driver.PerformActions(new List<ActionSequence> { actions });
            }).ConfigureAwait(false);

            data.Logger.Log("Swipe completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Long presses at coordinates.
        /// </summary>
        [Block("Long presses at specific coordinates", name = "Android Long Press")]
        public static async Task AndroidLongPress(BotData data,
            int x, int y,
            int durationMs = 1000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Long pressing at ({x}, {y}) for {durationMs}ms", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var finger = new PointerInputDevice(PointerKind.Touch, "finger");
                var actions = new ActionSequence(finger);

                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, x, y, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(MouseButton.Left));
                actions.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(durationMs)));
                actions.AddAction(finger.CreatePointerUp(MouseButton.Left));

                driver.PerformActions(new List<ActionSequence> { actions });
            }).ConfigureAwait(false);

            data.Logger.Log("Long press completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Gets the page source (UI hierarchy XML).
        /// </summary>
        [Block("Gets the Android UI hierarchy (page source)", name = "Android Get Source")]
        public static async Task<string> AndroidGetSource(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log("Getting UI hierarchy...", LogColors.LimeGreen);

            var source = await Task.Run(() => driver.PageSource).ConfigureAwait(false);

            data.Logger.Log($"UI hierarchy retrieved ({source.Length} chars)", LogColors.LimeGreen);

            return source;
        }

        /// <summary>
        /// Waits for a specified duration.
        /// </summary>
        [Block("Waits for specified milliseconds", name = "Android Wait")]
        public static async Task AndroidWait(BotData data, int milliseconds)
        {
            data.Logger.LogHeader();

            data.Logger.Log($"Waiting for {milliseconds}ms...", LogColors.LimeGreen);

            await Task.Delay(milliseconds).ConfigureAwait(false);

            data.Logger.Log("Wait completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Executes an ADB shell command.
        /// </summary>
        [Block("Executes an ADB shell command", name = "Android Shell")]
        public static async Task<string> AndroidShell(BotData data, string command)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Executing shell command: {command}", LogColors.LimeGreen);

            var result = await Task.Run(() =>
            {
                var output = driver.ExecuteScript("mobile: shell", new Dictionary<string, object>
                {
                    { "command", command }
                });
                return output?.ToString() ?? string.Empty;
            }).ConfigureAwait(false);

            data.Logger.Log($"Shell output: {result}", LogColors.LimeGreen);

            return result;
        }

        /// <summary>
        /// Gets the screen size.
        /// </summary>
        [Block("Gets the Android screen dimensions as [width, height]", name = "Android Get Screen Size")]
        public static async Task<List<string>> AndroidGetScreenSize(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            var size = await Task.Run(() => driver.Manage().Window.Size).ConfigureAwait(false);

            data.Logger.Log($"Screen size: {size.Width}x{size.Height}", LogColors.LimeGreen);

            return new List<string> { size.Width.ToString(), size.Height.ToString() };
        }

        /// <summary>
        /// Rotates the device.
        /// </summary>
        [Block("Rotates the Android device orientation", name = "Android Rotate")]
        public static async Task AndroidRotate(BotData data, bool landscape = true)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            var orientation = landscape ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;

            data.Logger.Log($"Rotating to: {orientation}", LogColors.LimeGreen);

            await Task.Run(() => driver.Orientation = orientation).ConfigureAwait(false);

            data.Logger.Log("Rotation completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Scrolls up on the screen.
        /// </summary>
        [Block("Scrolls up on the Android screen", name = "Android Scroll Up")]
        public static async Task AndroidScrollUp(BotData data, int distance = 500, int durationMs = 300)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var windowSize = driver.Manage().Window.Size;

            int centerX = windowSize.Width / 2;
            int startY = windowSize.Height / 2;
            int endY = startY + distance;

            data.Logger.Log($"Scrolling up by {distance}px", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var finger = new PointerInputDevice(PointerKind.Touch, "finger");
                var actions = new ActionSequence(finger);

                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, centerX, startY, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(MouseButton.Left));
                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, centerX, endY, TimeSpan.FromMilliseconds(durationMs)));
                actions.AddAction(finger.CreatePointerUp(MouseButton.Left));

                driver.PerformActions(new List<ActionSequence> { actions });
            }).ConfigureAwait(false);

            data.Logger.Log("Scroll up completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Scrolls down on the screen.
        /// </summary>
        [Block("Scrolls down on the Android screen", name = "Android Scroll Down")]
        public static async Task AndroidScrollDown(BotData data, int distance = 500, int durationMs = 300)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var windowSize = driver.Manage().Window.Size;

            int centerX = windowSize.Width / 2;
            int startY = windowSize.Height / 2;
            int endY = startY - distance;

            data.Logger.Log($"Scrolling down by {distance}px", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var finger = new PointerInputDevice(PointerKind.Touch, "finger");
                var actions = new ActionSequence(finger);

                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, centerX, startY, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(MouseButton.Left));
                actions.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, centerX, endY, TimeSpan.FromMilliseconds(durationMs)));
                actions.AddAction(finger.CreatePointerUp(MouseButton.Left));

                driver.PerformActions(new List<ActionSequence> { actions });
            }).ConfigureAwait(false);

            data.Logger.Log("Scroll down completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Hides the soft keyboard.
        /// </summary>
        [Block("Hides the Android soft keyboard", name = "Android Hide Keyboard")]
        public static async Task AndroidHideKeyboard(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log("Hiding keyboard...", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                try
                {
                    driver.HideKeyboard();
                }
                catch
                {
                    // Keyboard might not be visible, ignore
                }
            }).ConfigureAwait(false);

            data.Logger.Log("Keyboard hidden!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Checks if keyboard is shown.
        /// </summary>
        [Block("Checks if the Android soft keyboard is visible", name = "Android Is Keyboard Shown")]
        public static async Task<bool> AndroidIsKeyboardShown(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            var isShown = await Task.Run(() => driver.IsKeyboardShown()).ConfigureAwait(false);

            data.Logger.Log($"Keyboard shown: {isShown}", LogColors.LimeGreen);

            return isShown;
        }
    }
}
