using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Services.Modem
{
    /// <summary>
    /// Provides the same automation flow used by the native plugin hotkey to refresh the modem IP.
    /// </summary>
    public sealed class ModemRefreshService
    {
        private static readonly string[] ModemTogglePayloads =
        [
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=Only_LTE%0ALTE_preferred",
            "isTest=false&goformId=SET_BEARER_PREFERENCE&BearerPreference=NETWORK_auto%0ALTE_preferred"
        ];

        public async Task<ModemRefreshResult> RefreshAsync(ModemRefreshRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var logs = new List<string>();

            var (baseUri, username, password) = PrepareCredentials(request);
            logs.Add($"Target: {baseUri}");

            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            ConfigureModemClient(client);

            try
            {
                var loginPayload = BuildLoginPayload(username, password);
                logs.Add("Sending login request.");
                var loginResponse = await SendModemRequestAsync(client, baseUri, loginPayload, cancellationToken).ConfigureAwait(false);
                var loginBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logs.Add($"Login response {(int)loginResponse.StatusCode}: {Summarize(loginBody)}");

                loginResponse.EnsureSuccessStatusCode();

                var sessionCookie = FindSessionCookie(cookieContainer, baseUri);
                if (sessionCookie == null)
                {
                    throw new InvalidOperationException("Modem did not return a session cookie.");
                }

                var successCount = 0;
                foreach (var payload in ModemTogglePayloads.OrderBy(_ => Random.Shared.Next()))
                {
                    var preference = ExtractPreferenceName(payload);
                    logs.Add($"Applying preference '{preference}'.");
                    var response = await SendModemRequestAsync(client, baseUri, payload, cancellationToken).ConfigureAwait(false);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    logs.Add($"Response {(int)response.StatusCode}: {Summarize(responseBody)}");

                    response.EnsureSuccessStatusCode();

                    if (responseBody.Contains("success", StringComparison.OrdinalIgnoreCase))
                    {
                        successCount++;
                    }
                }

                var message = successCount > 0
                    ? "Network toggles sent to modem."
                    : "Modem did not acknowledge the toggle requests.";

                return new ModemRefreshResult(successCount > 0, message, logs, baseUri.ToString(), username);
            }
            catch (Exception ex)
            {
                logs.Add($"Error: {ex.Message}");
                return new ModemRefreshResult(false, $"Failed: {ex.Message}", logs, baseUri.ToString(), username);
            }
        }

        private static (Uri baseUri, string username, string password) PrepareCredentials(ModemRefreshRequest request)
        {
            var addressText = (request.RouterAddress ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(addressText))
            {
                throw new ArgumentException("Router address is required.", nameof(request));
            }

            if (!addressText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                addressText = $"http://{addressText}";
            }

            if (!Uri.TryCreate(addressText, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("Router address is invalid.", nameof(request));
            }

            var username = string.IsNullOrWhiteSpace(request.Username) ? "admin" : request.Username.Trim();
            var password = string.IsNullOrWhiteSpace(request.Password)
                ? "admin"
                : request.Password.Trim();

            return (baseUri, username, password);
        }

        private static void ConfigureModemClient(HttpClient client)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.AcceptEncoding.Clear();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/100.0.4896.60 Safari/537.36");
        }

        private static async Task<HttpResponseMessage> SendModemRequestAsync(HttpClient client, Uri baseUri, string payload, CancellationToken cancellationToken)
        {
            var endpoint = new Uri(baseUri, "/goform/goform_set_cmd_process");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            request.Headers.TryAddWithoutValidation("Origin", baseUri.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(baseUri, "/");

            return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static Cookie? FindSessionCookie(CookieContainer container, Uri baseUri)
        {
            foreach (Cookie cookie in container.GetCookies(baseUri))
            {
                if (string.Equals(cookie.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie;
                }
            }

            return null;
        }

        private static string BuildLoginPayload(string username, string password)
        {
            var credential = $"{username}\n{password}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
            return $"isTest=false&goformId=LOGIN&password={Uri.EscapeDataString(base64)}";
        }

        private static string ExtractPreferenceName(string payload)
        {
            const string key = "BearerPreference=";
            var start = payload.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return payload;
            }

            var value = payload[(start + key.Length)..];
            var end = value.IndexOf('%');
            if (end >= 0)
            {
                value = value[..end];
            }

            return value.Replace('_', ' ');
        }

        private static string Summarize(string input)
        {
            var text = input.Trim();
            if (text.Length == 0)
            {
                return "(empty)";
            }

            return text.Length > 120 ? text[..120] + "..." : text;
        }
    }

    public sealed class ModemRefreshRequest
    {
        public string RouterAddress { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class ModemRefreshResult
    {
        public ModemRefreshResult(bool isSuccess, string statusMessage, IReadOnlyList<string> logs, string targetAddress, string username)
        {
            IsSuccess = isSuccess;
            StatusMessage = statusMessage;
            Logs = logs;
            TargetAddress = targetAddress;
            Username = username;
        }

        public bool IsSuccess { get; }
        public string StatusMessage { get; }
        public IReadOnlyList<string> Logs { get; }
        public string TargetAddress { get; }
        public string Username { get; }
    }
}
