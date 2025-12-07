using Microsoft.Playwright;
using NAudio.Wave;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {
        [Block("Solves CAPTCHA challenges using audio recognition", name = "Solve CAPTCHA")]
        public static async Task PlaywrightSolveCaptcha(BotData data, int timeoutSeconds = 120, bool useAudioRecognition = true, int checkboxTimeoutMilliseconds = 2000)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var startTime = DateTime.Now;

            try
            {
                data.Logger.Log("= Looking for CAPTCHA challenges...", LogColors.MediumPurple);

                // Wait for CAPTCHA to appear with timeout
                while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                {
                    // Enhanced reCAPTCHA detection - check multiple patterns and nested iframes
                    var recaptchaFound = await DetectRecaptcha(page, data);
                    if (recaptchaFound)
                    {
                        data.Logger.Log("=Ļ Found reCAPTCHA challenge", LogColors.MediumPurple);
                        await SolveRecaptcha(page, data, useAudioRecognition, checkboxTimeoutMilliseconds);
                        return;
                    }

                    await Task.Delay(1000); // Wait 1 second before checking again
                }

                data.Logger.Log("GŦ Timeout reached - no CAPTCHA found", LogColors.Orange);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"G CAPTCHA solving failed: {ex.Message}", LogColors.Red);
                throw;
            }
        }

        private static async Task<bool> DetectRecaptcha(IPage page, BotData data)
        {
            try
            {
                // Method 1: Direct iframe src detection
                var directFrames = await page.QuerySelectorAllAsync("iframe[src*='recaptcha'], iframe[src*='google.com/recaptcha']");
                if (directFrames.Count > 0)
                {
                    data.Logger.Log($"Found {directFrames.Count} reCAPTCHA iframes by src attribute", LogColors.MediumPurple);
                    return true;
                }

                // Method 2: Look for g-recaptcha elements
                var gRecaptchaElements = await page.QuerySelectorAllAsync(".g-recaptcha, [data-sitekey], #g-recaptcha-response");
                if (gRecaptchaElements.Count > 0)
                {
                    data.Logger.Log($"Found {gRecaptchaElements.Count} g-recaptcha elements", LogColors.MediumPurple);
                    return true;
                }

                // Method 3: Search all iframes recursively for reCAPTCHA content
                var allIframes = await page.QuerySelectorAllAsync("iframe");
                data.Logger.Log($"Searching through {allIframes.Count} iframes for reCAPTCHA content...", LogColors.MediumPurple);

                foreach (var iframeElement in allIframes)
                {
                    try
                    {
                        var frame = await iframeElement.ContentFrameAsync();
                        if (frame != null)
                        {
                            // Check for reCAPTCHA indicators in this frame
                            var recaptchaIndicators = await frame.QuerySelectorAllAsync(
                                ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                            if (recaptchaIndicators.Count > 0)
                            {
                                data.Logger.Log($"Found reCAPTCHA indicators in nested iframe", LogColors.MediumPurple);
                                return true;
                            }

                            // Recursively check nested iframes
                            var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                            foreach (var nestedIframe in nestedIframes)
                            {
                                try
                                {
                                    var nestedFrame = await nestedIframe.ContentFrameAsync();
                                    if (nestedFrame != null)
                                    {
                                        var nestedIndicators = await nestedFrame.QuerySelectorAllAsync(
                                            ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                            "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                                        if (nestedIndicators.Count > 0)
                                        {
                                            data.Logger.Log($"Found reCAPTCHA indicators in deeply nested iframe", LogColors.MediumPurple);
                                            return true;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking nested iframe: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"Error checking iframe: {ex.Message}", LogColors.Orange);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Error in reCAPTCHA detection: {ex.Message}", LogColors.Red);
                return false;
            }
        }

        private static async Task SolveRecaptcha(IPage page, BotData data, bool useAudioRecognition, int checkboxTimeoutMilliseconds = 2000)
        {
            try
            {
                // Enhanced iframe detection - look for all possible reCAPTCHA iframes
                var recaptchaFrames = await GetAllRecaptchaFrames(page, data);
                if (recaptchaFrames.Count == 0)
                {
                    data.Logger.Log("G reCAPTCHA iframe not found", LogColors.Red);
                    return;
                }

                data.Logger.Log($"=Ļ Found {recaptchaFrames.Count} reCAPTCHA iframes", LogColors.MediumPurple);

                // Try each frame to find the main reCAPTCHA frame
                IFrame? mainFrame = null;
                foreach (var frameElement in recaptchaFrames)
                {
                    var frame = await frameElement.ContentFrameAsync();
                    if (frame != null)
                    {
                        // Check if this frame contains the checkbox
                        var frameCheckbox = await frame.QuerySelectorAsync(".rc-anchor-input, .recaptcha-checkbox, .recaptcha-checkbox-checkmark");
                        if (frameCheckbox != null)
                        {
                            mainFrame = frame;
                            break;
                        }
                    }
                }

                if (mainFrame == null)
                {
                    data.Logger.Log("G Could not find reCAPTCHA main frame with checkbox", LogColors.Red);
                    return;
                }

                // Look for checkbox in the main frame
                var checkbox = await mainFrame.QuerySelectorAsync(".rc-anchor-input, .recaptcha-checkbox, .recaptcha-checkbox-checkmark");
                if (checkbox != null)
                {
                    data.Logger.Log("=n+ Clicking reCAPTCHA checkbox...", LogColors.MediumPurple);
                    await checkbox.ClickAsync();
                    await Task.Delay(checkboxTimeoutMilliseconds);

                    // Check if audio challenge is available
                    if (useAudioRecognition)
                    {
                        await TryAudioChallenge(mainFrame, data);

                        // Enhanced challenge frame detection after clicking checkbox
                        await Task.Delay(2000); // Increased delay for frame to load

                        // Look for challenge frames with multiple selectors
                        var challengeFrameSelectors = new[]
                        {
                            "iframe[src*='recaptcha/api2/bframe']",
                            "iframe[title='recaptcha challenge']",
                            "iframe[src*='bframe']",
                            "iframe[name*='c-']",
                            "iframe[src*='challenge']",
                            "iframe[title*='challenge']",
                            "iframe[src*='recaptcha/api2/anchor']"
                        };

                        var challengeFrames = new List<IElementHandle>();
                        foreach (var selector in challengeFrameSelectors)
                        {
                            var frames = await page.QuerySelectorAllAsync(selector);
                            foreach (var frame in frames)
                            {
                                if (!challengeFrames.Contains(frame))
                                {
                                    challengeFrames.Add(frame);
                                }
                            }
                        }

                        if (challengeFrames.Count > 0)
                        {
                            data.Logger.Log($"=Ļ Found {challengeFrames.Count} challenge frame(s) after clicking checkbox", LogColors.MediumPurple);
                            foreach (var challengeFrameElement in challengeFrames)
                            {
                                var challengeFrame = await challengeFrameElement.ContentFrameAsync();
                                if (challengeFrame != null)
                                {
                                    await TryAudioChallenge(challengeFrame, data);
                                }
                            }
                        }
                        else
                        {
                            data.Logger.Log("= No challenge frames found, trying to find audio button in all frames", LogColors.Orange);
                            // Search all iframes on the page for audio challenge button
                            var allIframes = await page.QuerySelectorAllAsync("iframe");
                            foreach (var iframeElement in allIframes)
                            {
                                try
                                {
                                    var frame = await iframeElement.ContentFrameAsync();
                                    if (frame != null)
                                    {
                                        var audioButton = await FindAudioChallengeButton(frame, data);
                                        if (audioButton != null)
                                        {
                                            data.Logger.Log("=Ļ Found audio button in alternative frame", LogColors.MediumPurple);
                                            await TryAudioChallenge(frame, data);
                                            break; // Found and processed, exit loop
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking iframe for audio button: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                }
                else
                {
                    data.Logger.Log("G reCAPTCHA checkbox not found", LogColors.Red);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"G reCAPTCHA solving failed: {ex.Message}", LogColors.Red);
            }
        }

        private static async Task<List<IElementHandle>> GetAllRecaptchaFrames(IPage page, BotData data)
        {
            var allFrames = new List<IElementHandle>();

            try
            {
                // Method 1: Direct iframe src detection
                var directFrames = await page.QuerySelectorAllAsync("iframe[src*='recaptcha'], iframe[src*='google.com/recaptcha']");
                allFrames.AddRange(directFrames);

                // Method 2: Search all iframes for reCAPTCHA content
                var allIframes = await page.QuerySelectorAllAsync("iframe");

                foreach (var iframeElement in allIframes)
                {
                    try
                    {
                        var frame = await iframeElement.ContentFrameAsync();
                        if (frame != null)
                        {
                            // Check for reCAPTCHA indicators in this frame
                            var recaptchaIndicators = await frame.QuerySelectorAllAsync(
                                ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                            if (recaptchaIndicators.Count > 0 && !allFrames.Contains(iframeElement))
                            {
                                allFrames.Add(iframeElement);
                            }

                            // Check nested iframes
                            var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                            foreach (var nestedIframe in nestedIframes)
                            {
                                try
                                {
                                    var nestedFrame = await nestedIframe.ContentFrameAsync();
                                    if (nestedFrame != null)
                                    {
                                        var nestedIndicators = await nestedFrame.QuerySelectorAllAsync(
                                            ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                            "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                                        if (nestedIndicators.Count > 0 && !allFrames.Contains(nestedIframe))
                                        {
                                            allFrames.Add(nestedIframe);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking nested iframe: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"Error checking iframe: {ex.Message}", LogColors.Orange);
                    }
                }

                return allFrames;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Error getting reCAPTCHA frames: {ex.Message}", LogColors.Red);
                return allFrames;
            }
        }




        private static async Task<string> ProcessAudioChallenge(string audioUrl, BotData data)
        {
            var tempDir = Path.GetTempPath();
            var audioPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.mp3");
            var wavPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.wav");

            try
            {
                // Download audio efficiently
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var audioBytes = await httpClient.GetByteArrayAsync(audioUrl);
                await File.WriteAllBytesAsync(audioPath, audioBytes);

                // Quick format detection
                string format = "MP3"; // Default for reCAPTCHA
                if (audioBytes.Length >= 4)
                {
                    var header = System.Text.Encoding.ASCII.GetString(audioBytes, 0, 4);
                    if (header.StartsWith("ID3") || (audioBytes[0] == 0xFF && (audioBytes[1] & 0xE0) == 0xE0))
                        format = "MP3";
                    else if (header == "RIFF")
                        format = "WAV";
                    else if (header == "OggS")
                        format = "OGG";
                }

                // Convert to WAV efficiently
                WaveStream audioStream = null;
                try
                {
                    audioStream = format switch
                    {
                        "MP3" => new Mp3FileReader(audioPath),
                        "WAV" => new WaveFileReader(audioPath),
                        _ => new Mp3FileReader(audioPath) // Default to MP3
                    };
                }
                catch
                {
                    // Fallback to raw PCM
                    var waveFormat = new WaveFormat(16000, 16, 1);
                    audioStream = new RawSourceWaveStream(new MemoryStream(audioBytes), waveFormat);
                }

                if (audioStream != null)
                {
                    using var waveFileWriter = new WaveFileWriter(wavPath, audioStream.WaveFormat);
                    await Task.Run(() => audioStream.CopyTo(waveFileWriter));
                    audioStream.Dispose();
                }

                // Speech recognition (simplified)
                using var speechRecognition = new SpeechRecognitionEngine();
                speechRecognition.LoadGrammar(new DictationGrammar());

                string recognizedText = "";
                speechRecognition.SpeechRecognized += (sender, e) => recognizedText = e.Result.Text;
                speechRecognition.SetInputToWaveFile(wavPath);

                // Try recognition (max 2 attempts)
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var result = speechRecognition.Recognize();
                    if (result != null)
                    {
                        recognizedText = result.Text;
                        data.Logger.Log($"= Recognized: {recognizedText}", LogColors.MediumPurple);
                        break;
                    }

                    if (attempt == 2 && string.IsNullOrEmpty(recognizedText))
                    {
                        // Quick async attempt on last try
                        var completed = new TaskCompletionSource<bool>();
                        speechRecognition.SpeechRecognized += (sender, e) => { recognizedText = e.Result.Text; completed.TrySetResult(true); };
                        speechRecognition.RecognizeAsync(RecognizeMode.Single);

                        var timeout = await Task.WhenAny(completed.Task, Task.Delay(3000)) != completed.Task;
                        if (!timeout && !string.IsNullOrEmpty(recognizedText))
                        {
                            data.Logger.Log($"= Recognized: {recognizedText}", LogColors.MediumPurple);
                        }
                    }
                }

                return recognizedText;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"G Audio processing failed: {ex.Message}", LogColors.Red);
                return "";
            }
            finally
            {
                // Clean up temp files
                try
                {
                    if (File.Exists(audioPath)) File.Delete(audioPath);
                    if (File.Exists(wavPath)) File.Delete(wavPath);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private static async Task TryAudioChallenge(IFrame frame, BotData data)
        {
            try
            {
                var audioButton = await FindAudioChallengeButton(frame, data);
                if (audioButton == null) return;

                data.Logger.Log("= Clicking audio challenge button...", LogColors.MediumPurple);
                await audioButton.ClickAsync();
                await Task.Delay(2500);

                var audioInterfaceElements = await FindAudioInterfaceElements(frame, data);

                if (audioInterfaceElements.audioSource != null || audioInterfaceElements.audioElement != null || audioInterfaceElements.downloadLink != null)
                {
                    string audioUrl = audioInterfaceElements.audioSource?.GetAttributeAsync("src")?.Result ??
                                     audioInterfaceElements.audioElement?.GetAttributeAsync("src")?.Result ??
                                     audioInterfaceElements.downloadLink?.GetAttributeAsync("href")?.Result ?? "";

                    if (!string.IsNullOrEmpty(audioUrl))
                    {
                        if (!audioUrl.StartsWith("http"))
                            audioUrl = "https://www.google.com" + audioUrl;

                        data.Logger.Log($"=Ħ Audio URL: {audioUrl}", LogColors.MediumPurple);

                        string recognizedText = await ProcessAudioChallenge(audioUrl, data);
                        if (string.IsNullOrEmpty(recognizedText)) return;

                        data.Logger.Log($"= Entering audio response: {recognizedText}", LogColors.MediumPurple);

                        var audioResponseElements = await FindAudioResponseElements(frame, data);
                        if (audioResponseElements.inputField != null)
                        {
                            await audioResponseElements.inputField.FillAsync(recognizedText);

                            if (audioResponseElements.verifyButton != null)
                            {
                                await audioResponseElements.verifyButton.ClickAsync();
                                data.Logger.Log("G Audio challenge submitted", LogColors.Green);
                            }
                            else
                            {
                                await audioResponseElements.inputField.PressAsync("Enter");
                                data.Logger.Log("G Audio challenge submitted", LogColors.Green);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"G Audio challenge failed: {ex.Message}", LogColors.Red);
            }
        }

        private static async Task<IElementHandle?> FindAudioChallengeButton(IFrame frame, BotData data)
        {
            try
            {
                var audioButtonSelectors = new[]
                {
                    "button#recaptcha-audio-button",
                    "button[aria-label*='audio']",
                    "button[title*='audio']",
                    "button.rc-button-audio",
                    "#recaptcha-audio-button",
                    ".rc-button-audio",
                    "[role='button'][aria-label*='audio']"
                };

                // Search current frame
                foreach (var selector in audioButtonSelectors)
                {
                    try
                    {
                        var button = await frame.QuerySelectorAsync(selector);
                        if (button != null && await button.IsVisibleAsync())
                            return button;
                    }
                    catch { /* Continue to next selector */ }
                }

                // Search nested iframes
                var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                foreach (var nestedIframe in nestedIframes)
                {
                    try
                    {
                        var nestedFrame = await nestedIframe.ContentFrameAsync();
                        if (nestedFrame != null)
                        {
                            foreach (var selector in audioButtonSelectors)
                            {
                                try
                                {
                                    var button = await nestedFrame.QuerySelectorAsync(selector);
                                    if (button != null && await button.IsVisibleAsync())
                                        return button;
                                }
                                catch { /* Continue to next selector */ }
                            }
                        }
                    }
                    catch { /* Continue to next iframe */ }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<(IElementHandle? audioSource, IElementHandle? audioElement, IElementHandle? downloadLink)> FindAudioInterfaceElements(IFrame frame, BotData data)
        {
            try
            {
                var audioSourceSelectors = new[]
                {
                    "#audio-source",
                    ".rc-audiochallenge-tdownload-link",
                    "source[type*='audio']",
                    "[src*='recaptcha/api2/payload/audio']"
                };

                var audioElementSelectors = new[]
                {
                    "audio",
                    "audio[controls]",
                    ".rc-audiochallenge-control"
                };

                var downloadLinkSelectors = new[]
                {
                    "a[href*='audio']",
                    "a[href*='payload']",
                    ".rc-audiochallenge-tdownload-link",
                    "[href*='recaptcha/api2/payload']"
                };

                IElementHandle audioSource = null;
                IElementHandle audioElement = null;
                IElementHandle downloadLink = null;

                // Search current frame
                foreach (var selector in audioSourceSelectors)
                {
                    try
                    {
                        audioSource = await frame.QuerySelectorAsync(selector);
                        if (audioSource != null) break;
                    }
                    catch { /* Continue */ }
                }

                foreach (var selector in audioElementSelectors)
                {
                    try
                    {
                        audioElement = await frame.QuerySelectorAsync(selector);
                        if (audioElement != null) break;
                    }
                    catch { /* Continue */ }
                }

                foreach (var selector in downloadLinkSelectors)
                {
                    try
                    {
                        downloadLink = await frame.QuerySelectorAsync(selector);
                        if (downloadLink != null) break;
                    }
                    catch { /* Continue */ }
                }

                // Search nested iframes if needed
                if (audioSource == null && audioElement == null && downloadLink == null)
                {
                    var nestedFrames = frame.ChildFrames;
                    foreach (var nestedFrame in nestedFrames)
                    {
                        try
                        {
                            if (audioSource == null)
                            {
                                foreach (var selector in audioSourceSelectors)
                                {
                                    try
                                    {
                                        audioSource = await nestedFrame.QuerySelectorAsync(selector);
                                        if (audioSource != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (audioElement == null)
                            {
                                foreach (var selector in audioElementSelectors)
                                {
                                    try
                                    {
                                        audioElement = await nestedFrame.QuerySelectorAsync(selector);
                                        if (audioElement != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (downloadLink == null)
                            {
                                foreach (var selector in downloadLinkSelectors)
                                {
                                    try
                                    {
                                        downloadLink = await nestedFrame.QuerySelectorAsync(selector);
                                        if (downloadLink != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (audioSource != null && audioElement != null && downloadLink != null)
                                break;
                        }
                        catch { /* Continue to next iframe */ }
                    }
                }

                return (audioSource, audioElement, downloadLink);
            }
            catch
            {
                return (null, null, null);
            }
        }

        private static async Task<(IElementHandle? inputField, IElementHandle? verifyButton)> FindAudioResponseElements(IFrame frame, BotData data)
        {
            try
            {
                var inputSelectors = new[]
                {
                    "input#audio-response",
                    "input[name*='audio']",
                    "input[aria-label*='audio']",
                    "input[type='text']",
                    "input:not([type])",
                    "textarea[name*='audio']"
                };

                var buttonSelectors = new[]
                {
                    "button#recaptcha-verify-button",
                    "button[aria-label*='verify']",
                    "button[type='submit']",
                    "input[type='submit']"
                };

                // Helper method to check if element is visible
                async Task<bool> IsElementVisible(IElementHandle element)
                {
                    try
                    {
                        return await element.IsVisibleAsync();
                    }
                    catch
                    {
                        return false;
                    }
                }

                // Helper method to find elements in a frame with visibility check
                async Task<(IElementHandle? input, IElementHandle? button)> FindElementsInFrame(IFrame searchFrame, string frameDescription)
                {
                    IElementHandle? foundInput = null;
                    IElementHandle? foundButton = null;

                    // Search for input field
                    foreach (var selector in inputSelectors)
                    {
                        try
                        {
                            var element = await searchFrame.QuerySelectorAsync(selector);
                            if (element != null && await IsElementVisible(element))
                            {
                                foundInput = element;
                                break;
                            }
                        }
                        catch { }
                    }

                    // Search for verify button
                    foreach (var selector in buttonSelectors)
                    {
                        try
                        {
                            var element = await searchFrame.QuerySelectorAsync(selector);
                            if (element != null && await IsElementVisible(element))
                            {
                                foundButton = element;
                                break;
                            }
                        }
                        catch { }
                    }

                    return (foundInput, foundButton);
                }

                IElementHandle? inputField = null;
                IElementHandle? verifyButton = null;

                // Search in current frame first
                var currentFrameElements = await FindElementsInFrame(frame, "current frame");
                inputField = currentFrameElements.input;
                verifyButton = currentFrameElements.button;

                // If both elements found in current frame, we're done
                if (inputField != null && verifyButton != null)
                    return (inputField, verifyButton);

                // Search nested iframes
                if (inputField == null || verifyButton == null)
                {
                    // First pass: try to find both elements in the same nested frame
                    foreach (var childFrame in frame.ChildFrames)
                    {
                        var nestedFrameElements = await FindElementsInFrame(childFrame, "nested iframe");

                        // If both elements found in this frame, prioritize it
                        if (nestedFrameElements.input != null && nestedFrameElements.button != null)
                            return (nestedFrameElements.input, nestedFrameElements.button);

                        // Keep elements we found
                        if (inputField == null && nestedFrameElements.input != null)
                            inputField = nestedFrameElements.input;
                        if (verifyButton == null && nestedFrameElements.button != null)
                            verifyButton = nestedFrameElements.button;

                        // Search deeper nested frames
                        foreach (var deeperFrame in childFrame.ChildFrames)
                        {
                            var deeperFrameElements = await FindElementsInFrame(deeperFrame, "deeper nested iframe");

                            // If both elements found in this deeper frame, prioritize it
                            if (deeperFrameElements.input != null && deeperFrameElements.button != null)
                                return (deeperFrameElements.input, deeperFrameElements.button);

                            // Keep elements we found
                            if (inputField == null && deeperFrameElements.input != null)
                                inputField = deeperFrameElements.input;
                            if (verifyButton == null && deeperFrameElements.button != null)
                                verifyButton = deeperFrameElements.button;
                        }
                    }

                    // Second pass: search iframe elements if ChildFrames didn't work
                    if (inputField == null || verifyButton == null)
                    {
                        var iframes = await frame.QuerySelectorAllAsync("iframe");
                        foreach (var iframe in iframes)
                        {
                            try
                            {
                                var nestedFrame = await iframe.ContentFrameAsync();
                                if (nestedFrame == null) continue;

                                var iframeElements = await FindElementsInFrame(nestedFrame, "iframe content");

                                // If both elements found in this iframe, prioritize it
                                if (iframeElements.input != null && iframeElements.button != null)
                                    return (iframeElements.input, iframeElements.button);

                                // Keep elements we found
                                if (inputField == null && iframeElements.input != null)
                                    inputField = iframeElements.input;
                                if (verifyButton == null && iframeElements.button != null)
                                    verifyButton = iframeElements.button;
                            }
                            catch { }
                        }
                    }
                }

                return (inputField, verifyButton);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
