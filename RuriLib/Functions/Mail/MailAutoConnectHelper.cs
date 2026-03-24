using MailKit;
using MailKit.Net.Proxy;
using MailKit.Security;
using RuriLib.Functions.Http;
using RuriLib.Functions.Networking;
using RuriLib.Http.Models;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace RuriLib.Functions.Mail
{
    public sealed class MailAutoConnectOptions<TClient> where TClient : MailService
    {
        public required string ClientObjectKey { get; init; }
        public required string LogColor { get; init; }
        public required IReadOnlyList<string> CandidateSubdomains { get; init; }
        public required IReadOnlyList<int> CandidatePorts { get; init; }
        public required Func<string, Task<IEnumerable<HostEntry>>> GetKnownServersAsync { get; init; }
        public required Func<string, HostEntry, Task> CacheConnectedServerAsync { get; init; }
        public required Func<string, List<HostEntry>> ParseAutoconfig { get; init; }
        public Func<TClient, Task<bool>>? ValidateConnectionAsync { get; init; }
    }

    public static class MailAutoConnectHelper
    {
        public static async Task AutoConnectAsync<TClient>(BotData data, TClient client, string email, MailAutoConnectOptions<TClient> options)
            where TClient : MailService
        {
            data.SetObject(options.ClientObjectKey, client);

            var domain = email.Split('@')[1];

            if (await TryCandidatesAsync(data, client, domain,
                await options.GetKnownServersAsync(domain).ConfigureAwait(false), options).ConfigureAwait(false))
            {
                return;
            }

            var thunderbirdUrl = $"https://live.mozillamessaging.com/autoconfig/v1.1/{domain}";
            if (await TryCandidatesAsync(data, client, domain,
                await TryGetAutoconfigCandidatesAsync(data, thunderbirdUrl, null, options).ConfigureAwait(false), options).ConfigureAwait(false))
            {
                return;
            }

            var autoconfigUrl = $"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={email}";
            var autoconfigUrlUnsecure = $"http://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={email}";
            if (await TryCandidatesAsync(data, client, domain,
                await TryGetAutoconfigCandidatesAsync(data, autoconfigUrl, autoconfigUrlUnsecure, options).ConfigureAwait(false), options).ConfigureAwait(false))
            {
                return;
            }

            var wellKnownUrl = $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml";
            var wellKnownUrlUnsecure = $"http://{domain}/.well-known/autoconfig/mail/config-v1.1.xml";
            if (await TryCandidatesAsync(data, client, domain,
                await TryGetAutoconfigCandidatesAsync(data, wellKnownUrl, wellKnownUrlUnsecure, options).ConfigureAwait(false), options).ConfigureAwait(false))
            {
                return;
            }

            if (await TryCandidatesAsync(data, client, domain, BuildSubdomainCandidates(domain, options), options).ConfigureAwait(false))
            {
                return;
            }

            if (await TryCandidatesAsync(data, client, domain,
                await TryGetMxCandidatesAsync(data, domain, options).ConfigureAwait(false), options).ConfigureAwait(false))
            {
                return;
            }

            throw new Exception("Exhausted all possibilities, failed to connect!");
        }

        public static IProxyClient MapProxyClient(BotData data)
        {
            if (data.Proxy is null)
            {
                throw new InvalidOperationException("A proxy must be available to map a mail proxy client.");
            }

            if (data.Proxy.NeedsAuthentication)
            {
                var credentials = new NetworkCredential(data.Proxy.Username, data.Proxy.Password);

                return data.Proxy.Type switch
                {
                    Models.Proxies.ProxyType.Http => new HttpProxyClient(data.Proxy.Host, data.Proxy.Port, credentials),
                    Models.Proxies.ProxyType.Socks4 => new Socks4Client(data.Proxy.Host, data.Proxy.Port, credentials),
                    Models.Proxies.ProxyType.Socks4a => new Socks4aClient(data.Proxy.Host, data.Proxy.Port, credentials),
                    Models.Proxies.ProxyType.Socks5 => new Socks5Client(data.Proxy.Host, data.Proxy.Port, credentials),
                    _ => throw new NotImplementedException(),
                };
            }

            return data.Proxy.Type switch
            {
                Models.Proxies.ProxyType.Http => new HttpProxyClient(data.Proxy.Host, data.Proxy.Port),
                Models.Proxies.ProxyType.Socks4 => new Socks4Client(data.Proxy.Host, data.Proxy.Port),
                Models.Proxies.ProxyType.Socks4a => new Socks4aClient(data.Proxy.Host, data.Proxy.Port),
                Models.Proxies.ProxyType.Socks5 => new Socks5Client(data.Proxy.Host, data.Proxy.Port),
                _ => throw new NotImplementedException(),
            };
        }

        public static async Task<string> GetStringAsync(BotData data, string url)
        {
            using var httpClient = HttpFactory.GetRLHttpClient(data.Proxy, new HttpOptions
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(30000),
                ReadWriteTimeout = TimeSpan.FromMilliseconds(30000)
            });

            using var request = new HttpRequest
            {
                Uri = new Uri(url)
            };

            using var response = await httpClient.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(data.CancellationToken).ConfigureAwait(false);
        }

        public static ProtocolLogger InitLogger(BotData data, string loggerStreamKey, string loggerKey)
        {
            var stream = new MemoryStream();
            var protocolLogger = new ProtocolLogger(stream, true);
            data.SetObject(loggerStreamKey, stream);
            data.SetObject(loggerKey, protocolLogger);

            return protocolLogger;
        }

        private static async Task<bool> TryCandidatesAsync<TClient>(BotData data, TClient client, string domain,
            IEnumerable<HostEntry> candidates, MailAutoConnectOptions<TClient> options) where TClient : MailService
        {
            foreach (var candidate in candidates)
            {
                if (await TryConnectAsync(data, client, domain, candidate, options).ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> TryConnectAsync<TClient>(BotData data, TClient client, string domain,
            HostEntry entry, MailAutoConnectOptions<TClient> options) where TClient : MailService
        {
            data.Logger.Log($"Trying {entry.Host} on port {entry.Port}...", options.LogColor);

            try
            {
                await client.ConnectAsync(entry.Host, entry.Port, SecureSocketOptions.Auto, data.CancellationToken).ConfigureAwait(false);
                data.Logger.Log($"Connected! SSL/TLS: {client.IsSecure}", options.LogColor);

                if (options.ValidateConnectionAsync != null &&
                    !await options.ValidateConnectionAsync(client).ConfigureAwait(false))
                {
                    return false;
                }

                await options.CacheConnectedServerAsync(domain, entry).ConfigureAwait(false);
                return true;
            }
            catch
            {
                data.Logger.Log("Failed!", options.LogColor);
            }

            return false;
        }

        private static async Task<List<HostEntry>> TryGetAutoconfigCandidatesAsync<TClient>(BotData data, string url,
            string? fallbackUrl, MailAutoConnectOptions<TClient> options) where TClient : MailService
        {
            try
            {
                string xml;

                try
                {
                    xml = await GetStringAsync(data, url).ConfigureAwait(false);
                }
                catch when (fallbackUrl != null)
                {
                    xml = await GetStringAsync(data, fallbackUrl).ConfigureAwait(false);
                }

                var candidates = options.ParseAutoconfig(xml);
                data.Logger.Log($"Queried {url} and got {candidates.Count} server(s)", options.LogColor);
                return candidates;
            }
            catch
            {
                var message = fallbackUrl is null
                    ? $"Failed to query {url}"
                    : $"Failed to query {url} (both https and http)";

                data.Logger.Log(message, options.LogColor);
                return new List<HostEntry>();
            }
        }

        private static List<HostEntry> BuildSubdomainCandidates<TClient>(string domain, MailAutoConnectOptions<TClient> options)
            where TClient : MailService
        {
            var candidates = new List<HostEntry>();

            AddHostCandidates(candidates, domain, options.CandidatePorts);

            foreach (var subdomain in options.CandidateSubdomains)
            {
                AddHostCandidates(candidates, $"{subdomain}.{domain}", options.CandidatePorts);
            }

            return candidates;
        }

        private static async Task<List<HostEntry>> TryGetMxCandidatesAsync<TClient>(BotData data, string domain,
            MailAutoConnectOptions<TClient> options) where TClient : MailService
        {
            var candidates = new List<HostEntry>();

            try
            {
                var mxRecords = await DnsLookup.FromGoogleAsync(domain, "MX", data.Proxy, 30000, data.CancellationToken).ConfigureAwait(false);
                foreach (var record in mxRecords)
                {
                    AddHostCandidates(candidates, record, options.CandidatePorts);
                }

                data.Logger.Log($"Queried the MX records and got {candidates.Count} server(s)", options.LogColor);
            }
            catch
            {
                data.Logger.Log("Failed to query the MX records", options.LogColor);
            }

            return candidates;
        }

        private static void AddHostCandidates(ICollection<HostEntry> candidates, string host, IEnumerable<int> ports)
        {
            foreach (var port in ports)
            {
                candidates.Add(new HostEntry(host, port));
            }
        }
    }
}
