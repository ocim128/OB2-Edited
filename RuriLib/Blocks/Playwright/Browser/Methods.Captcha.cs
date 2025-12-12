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
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {
        #region reCAPTCHA Selectors
        
        private static readonly string[] RecaptchaIndicatorSelectors =
        {
            ".rc-anchor", ".recaptcha-checkbox", "#recaptcha-anchor", ".g-recaptcha",
            "[data-sitekey]", "#g-recaptcha-response", ".rc-anchor-checkbox-holder"
        };

        private static readonly string[] RecaptchaIframeSelectors =
        {
            "iframe[src*='recaptcha']", "iframe[src*='google.com/recaptcha']"
        };

        private static readonly string[] ChallengeFrameSelectors =
        {
            "iframe[src*='recaptcha/api2/bframe']", "iframe[title='recaptcha challenge']",
            "iframe[src*='bframe']", "iframe[name*='c-']", "iframe[src*='challenge']",
            "iframe[title*='challenge']", "iframe[src*='recaptcha/api2/anchor']"
        };

        private static readonly string[] AudioButtonSelectors =
        {
            "button#recaptcha-audio-button", "button[aria-label*='audio']", "button[title*='audio']",
            "button.rc-button-audio", "#recaptcha-audio-button", ".rc-button-audio", "[role='button'][aria-label*='audio']"
        };

        private static readonly string[] AudioSourceSelectors =
        {
            "#audio-source", ".rc-audiochallenge-tdownload-link", "source[type*='audio']", "[src*='recaptcha/api2/payload/audio']"
        };

        private static readonly string[] AudioElementSelectors = { "audio", "audio[controls]", ".rc-audiochallenge-control" };

        private static readonly string[] DownloadLinkSelectors =
        {
            "a[href*='audio']", "a[href*='payload']", ".rc-audiochallenge-tdownload-link", "[href*='recaptcha/api2/payload']"
        };

        private static readonly string[] AudioInputSelectors =
        {
            "input#audio-response", "input[name*='audio']", "input[aria-label*='audio']",
            "input[type='text']", "input:not([type])", "textarea[name*='audio']"
        };

        private static readonly string[] VerifyButtonSelectors =
        {
            "button#recaptcha-verify-button", "button[aria-label*='verify']", "button[type='submit']", "input[type='submit']"
        };

        #endregion

        #region Public Methods

        [Block("Solves CAPTCHA challenges using audio recognition", name = "Solve CAPTCHA")]
        public static async Task PlaywrightSolveCaptcha(BotData data, int timeoutSeconds = 120, bool useAudioRecognition = true, int checkboxTimeoutMilliseconds = 2000)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var startTime = DateTime.Now;

            try
            {
                data.Logger.Log("🔍 Looking for CAPTCHA challenges...", LogColors.MediumPurple);

                while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                {
                    if (await DetectRecaptcha(page, data))
                    {
                        data.Logger.Log("🤖 Found reCAPTCHA challenge", LogColors.MediumPurple);
                        await SolveRecaptcha(page, data, useAudioRecognition, checkboxTimeoutMilliseconds);
                        return;
                    }
                    await Task.Delay(1000);
                }

                data.Logger.Log("⏱️ Timeout reached - no CAPTCHA found", LogColors.Orange);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ CAPTCHA solving failed: {ex.Message}", LogColors.Red);
                throw;
            }
        }

        #endregion

        #region Detection and Solving

        private static async Task<bool> DetectRecaptcha(IPage page, BotData data)
        {
            try
            {
                // Check for direct reCAPTCHA iframes
                if (await QueryAnySelectorAsync(page, RecaptchaIframeSelectors) != null)
                {
                    return true;
                }

                // Check for g-recaptcha elements on the page
                if (await QueryAnySelectorAsync(page, RecaptchaIndicatorSelectors) != null)
                {
                    return true;
                }

                // Search nested iframes for reCAPTCHA indicators
                return await SearchFramesForIndicators(page, RecaptchaIndicatorSelectors, maxDepth: 2);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Error in reCAPTCHA detection: {ex.Message}", LogColors.Red);
                return false;
            }
        }

        private static async Task SolveRecaptcha(IPage page, BotData data, bool useAudioRecognition, int checkboxTimeoutMilliseconds)
        {
            try
            {
                var mainFrame = await FindRecaptchaMainFrame(page, data);
                if (mainFrame == null)
                {
                    data.Logger.Log("❌ Could not find reCAPTCHA main frame", LogColors.Red);
                    return;
                }

                // Find and click the checkbox
                var checkbox = await QueryAnySelectorAsync(mainFrame, new[] { ".rc-anchor-input", ".recaptcha-checkbox", ".recaptcha-checkbox-checkmark" });
                if (checkbox == null)
                {
                    data.Logger.Log("❌ reCAPTCHA checkbox not found", LogColors.Red);
                    return;
                }

                data.Logger.Log("🖱️ Clicking reCAPTCHA checkbox...", LogColors.MediumPurple);
                await checkbox.ClickAsync();
                await Task.Delay(checkboxTimeoutMilliseconds);

                if (!useAudioRecognition) return;

                // Wait for challenge frame to appear
                await Task.Delay(2000);

                // Try to solve via audio challenge
                await TryAudioChallenge(page, mainFrame, data);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ reCAPTCHA solving failed: {ex.Message}", LogColors.Red);
            }
        }

        #endregion

        #region Audio Challenge

        private static async Task TryAudioChallenge(IPage page, IFrame mainFrame, BotData data)
        {
            // First check the main frame
            var audioButton = await FindVisibleElement(mainFrame, AudioButtonSelectors);

            // If not found, search challenge frames
            if (audioButton == null)
            {
                var challengeFrames = await GetFramesBySelectors(page, ChallengeFrameSelectors);
                foreach (var frame in challengeFrames)
                {
                    audioButton = await FindVisibleElement(frame, AudioButtonSelectors);
                    if (audioButton != null)
                    {
                        mainFrame = frame; // Switch to the challenge frame
                        break;
                    }
                }
            }

            if (audioButton == null)
            {
                data.Logger.Log("⚠️ Audio challenge button not found", LogColors.Orange);
                return;
            }

            data.Logger.Log("🔊 Clicking audio challenge button...", LogColors.MediumPurple);
            await audioButton.ClickAsync();
            await Task.Delay(2500);

            // Get audio URL
            var audioUrl = await GetAudioUrl(mainFrame, data);
            if (string.IsNullOrEmpty(audioUrl))
            {
                data.Logger.Log("❌ Could not find audio URL", LogColors.Red);
                return;
            }

            data.Logger.Log($"🎵 Audio URL: {audioUrl}", LogColors.MediumPurple);

            // Process audio and get recognized text
            var recognizedText = await ProcessAudioChallenge(audioUrl, data);
            if (string.IsNullOrEmpty(recognizedText))
            {
                data.Logger.Log("❌ Audio recognition failed", LogColors.Red);
                return;
            }

            // Submit the response
            await SubmitAudioResponse(mainFrame, recognizedText, data);
        }

        private static async Task<string?> GetAudioUrl(IFrame frame, BotData data)
        {
            // Try audio source first
            var element = await QueryAnySelectorAsync(frame, AudioSourceSelectors)
                       ?? await QueryAnySelectorAsync(frame, AudioElementSelectors)
                       ?? await QueryAnySelectorAsync(frame, DownloadLinkSelectors);

            if (element == null)
            {
                // Try nested frames
                foreach (var childFrame in frame.ChildFrames)
                {
                    element = await QueryAnySelectorAsync(childFrame, AudioSourceSelectors)
                           ?? await QueryAnySelectorAsync(childFrame, AudioElementSelectors)
                           ?? await QueryAnySelectorAsync(childFrame, DownloadLinkSelectors);
                    if (element != null) break;
                }
            }

            if (element == null) return null;

            var url = await element.GetAttributeAsync("src") ?? await element.GetAttributeAsync("href");
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http"))
            {
                url = "https://www.google.com" + url;
            }
            return url;
        }

        private static async Task SubmitAudioResponse(IFrame frame, string recognizedText, BotData data)
        {
            var inputField = await FindVisibleElement(frame, AudioInputSelectors);

            // Search child frames if not found
            if (inputField == null)
            {
                foreach (var childFrame in frame.ChildFrames)
                {
                    inputField = await FindVisibleElement(childFrame, AudioInputSelectors);
                    if (inputField != null)
                    {
                        frame = childFrame;
                        break;
                    }
                }
            }

            if (inputField == null)
            {
                data.Logger.Log("❌ Audio response input field not found", LogColors.Red);
                return;
            }

            data.Logger.Log($"📝 Entering audio response: {recognizedText}", LogColors.MediumPurple);
            await inputField.FillAsync(recognizedText);

            var verifyButton = await FindVisibleElement(frame, VerifyButtonSelectors);
            if (verifyButton != null)
            {
                await verifyButton.ClickAsync();
            }
            else
            {
                await inputField.PressAsync("Enter");
            }

            data.Logger.Log("✅ Audio challenge submitted", LogColors.Green);
        }

        private static async Task<string> ProcessAudioChallenge(string audioUrl, BotData data)
        {
            var tempDir = Path.GetTempPath();
            var audioPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.mp3");
            var wavPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.wav");

            try
            {
                // Download audio
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var audioBytes = await httpClient.GetByteArrayAsync(audioUrl);
                await File.WriteAllBytesAsync(audioPath, audioBytes);

                // Convert to WAV
                await ConvertToWav(audioPath, wavPath, audioBytes);

                // Speech recognition
                return RecognizeSpeech(wavPath, data);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Audio processing failed: {ex.Message}", LogColors.Red);
                return string.Empty;
            }
            finally
            {
                TryDeleteFile(audioPath);
                TryDeleteFile(wavPath);
            }
        }

        private static async Task ConvertToWav(string inputPath, string outputPath, byte[] audioBytes)
        {
            WaveStream? audioStream = null;
            try
            {
                // Detect format from header
                if (audioBytes.Length >= 4)
                {
                    var header = System.Text.Encoding.ASCII.GetString(audioBytes, 0, 4);
                    audioStream = header switch
                    {
                        var h when h == "RIFF" => new WaveFileReader(inputPath),
                        _ => new Mp3FileReader(inputPath) // Default to MP3
                    };
                }
                else
                {
                    audioStream = new Mp3FileReader(inputPath);
                }
            }
            catch
            {
                // Fallback to raw PCM
                audioStream = new RawSourceWaveStream(new MemoryStream(audioBytes), new WaveFormat(16000, 16, 1));
            }

            if (audioStream != null)
            {
                using var writer = new WaveFileWriter(outputPath, audioStream.WaveFormat);
                await Task.Run(() => audioStream.CopyTo(writer));
                audioStream.Dispose();
            }
        }

        private static string RecognizeSpeech(string wavPath, BotData data)
        {
            using var recognizer = new SpeechRecognitionEngine();
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToWaveFile(wavPath);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var result = recognizer.Recognize();
                if (result != null)
                {
                    data.Logger.Log($"🎤 Recognized: {result.Text}", LogColors.MediumPurple);
                    return result.Text;
                }
            }

            return string.Empty;
        }

        #endregion

        #region Frame Helpers

        private static async Task<IFrame?> FindRecaptchaMainFrame(IPage page, BotData data)
        {
            // Get all potential reCAPTCHA frames
            var frames = await GetFramesBySelectors(page, RecaptchaIframeSelectors);

            foreach (var frame in frames)
            {
                // Check if this frame contains the checkbox
                var checkbox = await QueryAnySelectorAsync(frame, new[] { ".rc-anchor-input", ".recaptcha-checkbox", ".recaptcha-checkbox-checkmark" });
                if (checkbox != null)
                {
                    return frame;
                }
            }

            // Fallback: search all iframes
            var allIframes = await page.QuerySelectorAllAsync("iframe");
            foreach (var iframeElement in allIframes)
            {
                try
                {
                    var frame = await iframeElement.ContentFrameAsync();
                    if (frame != null)
                    {
                        var checkbox = await QueryAnySelectorAsync(frame, new[] { ".rc-anchor-input", ".recaptcha-checkbox" });
                        if (checkbox != null) return frame;
                    }
                }
                catch { /* Continue */ }
            }

            return null;
        }

        private static async Task<List<IFrame>> GetFramesBySelectors(IPage page, string[] selectors)
        {
            var frames = new List<IFrame>();

            foreach (var selector in selectors)
            {
                try
                {
                    var elements = await page.QuerySelectorAllAsync(selector);
                    foreach (var element in elements)
                    {
                        try
                        {
                            var frame = await element.ContentFrameAsync();
                            if (frame != null && !frames.Contains(frame))
                            {
                                frames.Add(frame);
                            }
                        }
                        catch { /* Continue */ }
                    }
                }
                catch { /* Continue */ }
            }

            return frames;
        }

        private static async Task<bool> SearchFramesForIndicators(IPage page, string[] selectors, int maxDepth)
        {
            var allIframes = await page.QuerySelectorAllAsync("iframe");

            foreach (var iframeElement in allIframes)
            {
                try
                {
                    var frame = await iframeElement.ContentFrameAsync();
                    if (frame == null) continue;

                    if (await QueryAnySelectorAsync(frame, selectors) != null)
                    {
                        return true;
                    }

                    // Check one level of nested iframes
                    if (maxDepth > 1)
                    {
                        foreach (var childFrame in frame.ChildFrames)
                        {
                            if (await QueryAnySelectorAsync(childFrame, selectors) != null)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch { /* Continue */ }
            }

            return false;
        }

        #endregion

        #region Element Query Helpers

        private static async Task<IElementHandle?> QueryAnySelectorAsync(IPage page, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                try
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null) return element;
                }
                catch { /* Continue */ }
            }
            return null;
        }

        private static async Task<IElementHandle?> QueryAnySelectorAsync(IFrame frame, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                try
                {
                    var element = await frame.QuerySelectorAsync(selector);
                    if (element != null) return element;
                }
                catch { /* Continue */ }
            }
            return null;
        }

        private static async Task<IElementHandle?> FindVisibleElement(IFrame frame, string[] selectors)
        {
            foreach (var selector in selectors)
            {
                try
                {
                    var element = await frame.QuerySelectorAsync(selector);
                    if (element != null && await element.IsVisibleAsync())
                    {
                        return element;
                    }
                }
                catch { /* Continue */ }
            }
            return null;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* Ignore */ }
        }

        #endregion
    }
}
