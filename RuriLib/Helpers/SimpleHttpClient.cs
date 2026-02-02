using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Provides a shared HttpClient instance for simple HTTP operations.
    /// Avoids socket exhaustion from creating new HttpClient instances repeatedly.
    /// </summary>
    public static class SimpleHttpClient
    {
        private static readonly Lazy<HttpClient> _instance = new(() => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        });

        /// <summary>
        /// Gets the shared HttpClient instance.
        /// </summary>
        public static HttpClient Instance => _instance.Value;

        /// <summary>
        /// Posts JSON data to a webhook URL.
        /// </summary>
        /// <param name="url">The webhook URL.</param>
        /// <param name="data">The data to serialize as JSON.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The HTTP response.</returns>
        public static async Task<HttpResponseMessage> PostJsonAsync(string url, object data, CancellationToken cancellationToken = default)
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await Instance.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Posts JSON data to a Discord webhook.
        /// </summary>
        /// <param name="webhookUrl">The Discord webhook URL.</param>
        /// <param name="message">The message content.</param>
        /// <param name="username">Optional username override.</param>
        /// <param name="avatarUrl">Optional avatar URL override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task PostToDiscordAsync(
            string webhookUrl, 
            string message, 
            string? username = null, 
            string? avatarUrl = null,
            CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object>
            {
                ["content"] = message
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                payload["username"] = username;
            }

            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                payload["avatar_url"] = avatarUrl;
            }

            await PostJsonAsync(webhookUrl, payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a message via Telegram Bot API.
        /// </summary>
        /// <param name="botToken">The Telegram bot token.</param>
        /// <param name="chatId">The chat ID to send to.</param>
        /// <param name="message">The message text.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task SendTelegramMessageAsync(
            string botToken, 
            long chatId, 
            string message,
            CancellationToken cancellationToken = default)
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["text"] = message
            };

            await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs a simple GET request and returns the response body as string.
        /// </summary>
        /// <param name="url">The URL to fetch.</param>
        /// <param name="timeout">Optional timeout (uses default if not specified).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The response body as string.</returns>
        public static async Task<string> GetStringAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
            {
                cts.CancelAfter(timeout.Value);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            
            var response = await Instance.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }
}
