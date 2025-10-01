using NAudio.Wave;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.IO;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Utility.Audio
{
    [BlockCategory("Audio", "Blocks for playing audio files", "#fad6a5")]
    public static class Methods
    {
        [Block("Plays a sound file from a path. If a bare filename is provided (e.g. 'ui-sound.mp3'), it is resolved relative to the app base folder.", name = "Play Sound")]
        public static async Task PlaySound(BotData data, [Variable] string path, bool waitForCompletion = true)
        {
            data.Logger.LogHeader();

            try
            {
                var resolved = ResolvePath(path ?? string.Empty);
                if (!File.Exists(resolved))
                {
                    data.Logger.LogError($"Sound file not found: {resolved}", new FileNotFoundException("Sound file not found", resolved));
                    return;
                }

                data.Logger.Log($"Playing sound: '{resolved}'", LogColors.DeepChampagne);

                if (!waitForCompletion)
                {
                    var token = data.CancellationToken;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(resolved);
                            using var outputDevice = new WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();

                            while (outputDevice.PlaybackState == PlaybackState.Playing && !token.IsCancellationRequested)
                            {
                                await Task.Delay(100, token).ConfigureAwait(false);
                            }

                            try { outputDevice.Stop(); } catch { /* ignore */ }
                        }
                        catch { /* background playback errors are non-fatal */ }
                    }, token);

                    return;
                }

                using (var audioFile = new AudioFileReader(resolved))
                using (var outputDevice = new WaveOutEvent())
                {
                    outputDevice.Init(audioFile);
                    outputDevice.Play();

                    while (outputDevice.PlaybackState == PlaybackState.Playing && !data.CancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, data.CancellationToken).ConfigureAwait(false);
                    }

                    try { outputDevice.Stop(); } catch { /* ignore */ }
                }
            }
            catch (OperationCanceledException)
            {
                // Honor cancellation, attempt to stop playback gracefully
                data.Logger.Log("Playback canceled", LogColors.DeepChampagne);
            }
            catch (Exception ex)
            {
                data.Logger.LogError($"Failed to play sound: {ex.Message}", ex);
            }
        }

        private static string ResolvePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            // If path is rooted or contains directory separators, use as-is; otherwise base dir
            if (Path.IsPathRooted(input) || input.Contains(Path.DirectorySeparatorChar) || input.Contains(Path.AltDirectorySeparatorChar))
            {
                return input;
            }

            var baseDir = AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? Directory.GetCurrentDirectory();
            return Path.Combine(baseDir, input);
        }
    }
}
