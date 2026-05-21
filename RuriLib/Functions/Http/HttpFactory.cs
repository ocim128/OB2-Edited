using RuriLib.Http;
using RuriLib.Models.Proxies;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace RuriLib.Functions.Http
{
    public class HttpFactory
    {
        private static readonly ConcurrentDictionary<string, Lazy<SharedHttpClientEntry>> SharedHttpClients = new();
        private static readonly Timer SharedHttpClientCleanupTimer = new(CleanupSharedHttpClients, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        private static readonly TimeSpan SharedHttpClientIdleTimeout = TimeSpan.FromMinutes(3);

        internal sealed class SharedHttpClientEntry : IDisposable
        {
            private readonly object sync = new();
            private bool disposed;
            private int activeLeases;

            public required HttpClient Client { get; init; }
            public DateTime LastAccessed { get; private set; } = DateTime.UtcNow;

            public SharedHttpClientLease Rent()
            {
                lock (sync)
                {
                    if (disposed)
                    {
                        throw new ObjectDisposedException(nameof(SharedHttpClientEntry));
                    }

                    activeLeases++;
                    LastAccessed = DateTime.UtcNow;
                    return new SharedHttpClientLease(this);
                }
            }

            public void Release()
            {
                lock (sync)
                {
                    if (activeLeases > 0)
                    {
                        activeLeases--;
                    }

                    LastAccessed = DateTime.UtcNow;
                }
            }

            public bool TryMarkForDisposal(DateTime cutoff)
            {
                lock (sync)
                {
                    if (disposed || activeLeases > 0 || LastAccessed >= cutoff)
                    {
                        return false;
                    }

                    disposed = true;
                    return true;
                }
            }

            public void Dispose() => Client.Dispose();
        }

        internal sealed class SharedHttpClientLease : IDisposable
        {
            private SharedHttpClientEntry? entry;

            internal SharedHttpClientLease(SharedHttpClientEntry entry)
                => this.entry = entry;

            public HttpClient Client => entry?.Client ?? throw new ObjectDisposedException(nameof(SharedHttpClientLease));

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref entry, null);
                current?.Release();
            }
        }

        public static ProxyClientHandler GetProxiedHandler(Proxy proxy, HttpOptions options, CookieContainer cookies)
        {
            var client = GetProxyClient(proxy, options);

            return new ProxyClientHandler(client)
            {
                AllowAutoRedirect = options.AutoRedirect,
                MaxNumberOfRedirects = options.MaxNumberOfRedirects,
                CookieContainer = cookies,
                UseCookies = cookies != null,
                SslProtocols = ToSslProtocols(options.SecurityProtocol),
                UseCustomCipherSuites = options.UseCustomCipherSuites,
                AllowedCipherSuites = options.CustomCipherSuites,
                CertRevocationMode = options.CertRevocationMode,
                ReadResponseContent = options.ReadResponseContent
            };
        }

        public static RuriLib.Http.RLHttpClient GetRLHttpClient(Proxy proxy, HttpOptions options)
        {
            var client = GetProxyClient(proxy, options);

            return new RuriLib.Http.RLHttpClient(client)
            {
                AllowAutoRedirect = options.AutoRedirect,
                MaxNumberOfRedirects = options.MaxNumberOfRedirects,
                SslProtocols = ToSslProtocols(options.SecurityProtocol),
                UseCustomCipherSuites = options.UseCustomCipherSuites,
                AllowedCipherSuites = options.CustomCipherSuites,
                CertRevocationMode = options.CertRevocationMode,
                ReadResponseContent = options.ReadResponseContent,
                ReceiveTimeout = NormalizeHandlerTimeout(options.ReadWriteTimeout, nameof(options.ReadWriteTimeout))
            };
        }

        public static HttpClient GetHttpClient(Proxy proxy, HttpOptions options, CookieContainer cookieContainer)
        {
            var handler = GetHttpMessageHandler(proxy, options, cookieContainer);

            return new HttpClient(handler)
            {
                Timeout = NormalizeHttpClientTimeout(options.ReadWriteTimeout)
            };
        }

        internal static SharedHttpClientLease RentSharedHttpClient(Proxy proxy, HttpOptions options)
        {
            var key = GenerateSharedHttpClientKey(proxy, options);

            while (true)
            {
                var lazy = SharedHttpClients.GetOrAdd(key, _ => new Lazy<SharedHttpClientEntry>(() => new SharedHttpClientEntry
                {
                    Client = GetHttpClient(proxy, options, null)
                }, LazyThreadSafetyMode.ExecutionAndPublication));

                try
                {
                    return lazy.Value.Rent();
                }
                catch (ObjectDisposedException)
                {
                    TryRemoveSharedHttpClient(key, lazy);
                }
                catch
                {
                    TryRemoveSharedHttpClient(key, lazy);
                    throw;
                }
            }
        }

        private static ProxyClient GetProxyClient(Proxy proxy, HttpOptions options)
        {
            ProxyClient client;

            if (proxy == null)
            {
                client = new NoProxyClient(new ProxySettings());
            }
            else
            {
                var settings = new ProxySettings()
                {
                    Host = proxy.Host,
                    Port = proxy.Port,
                    ConnectTimeout = options.ConnectTimeout,
                    ReadWriteTimeOut = options.ReadWriteTimeout
                };

                if (proxy.NeedsAuthentication)
                {
                    settings.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }

                client = proxy.Type switch
                {
                    ProxyType.Http => new HttpProxyClient(settings),
                    ProxyType.Socks4 => new Socks4ProxyClient(settings),
                    ProxyType.Socks4a => new Socks4aProxyClient(settings),
                    ProxyType.Socks5 => new Socks5ProxyClient(settings),
                    _ => throw new NotImplementedException()
                };
            }

            return client;
        }

        private static HttpMessageHandler GetHttpMessageHandler(Proxy proxy, HttpOptions options, CookieContainer cookieContainer)
        {
            HttpMessageHandler handler;

            if (proxy == null)
            {
                handler = new HttpClientHandler();
            }
            else
            {
                switch (proxy.Type)
                {
                    case ProxyType.Http:
                        handler = new HttpClientHandler()
                        {
                            Proxy = GetWebProxy(proxy)
                        };
                        break;

                    case ProxyType.Socks4:
                    case ProxyType.Socks4a:
                    case ProxyType.Socks5:
                        handler = new SocketsHttpHandler()
                        {
                            Proxy = GetWebProxy(proxy)
                        };
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            return ConfigureHttpMessageHandler(handler, options, cookieContainer);
        }

        private static WebProxy GetWebProxy(Proxy proxy)
        {
            var proxyCredentials = proxy.NeedsAuthentication
                ? new NetworkCredential(proxy.Username, proxy.Password)
                : null;

            var address = proxy.Type switch
            {
                ProxyType.Http => $"http://{proxy.Host}:{proxy.Port}",
                ProxyType.Socks4 => $"socks4://{proxy.Host}:{proxy.Port}",
                ProxyType.Socks4a => $"socks4a://{proxy.Host}:{proxy.Port}",
                ProxyType.Socks5 => $"socks5://{proxy.Host}:{proxy.Port}",
                _ => throw new NotImplementedException(),
            };

            return new WebProxy(address, true, null, proxyCredentials);
        }

        private static string GenerateSharedHttpClientKey(Proxy proxy, HttpOptions options)
        {
            var proxyKey = proxy == null
                ? "noproxy"
                : $"{proxy.Type}:{proxy.Host}:{proxy.Port}:{(proxy.NeedsAuthentication ? $"{proxy.Username}:{proxy.Password}" : "")}";

            var cipherKey = options.UseCustomCipherSuites && options.CustomCipherSuites is { Length: > 0 }
                ? string.Join(",", options.CustomCipherSuites.Select(c => c.ToString()))
                : "default";

            return string.Join("|",
                proxyKey,
                options.SecurityProtocol,
                options.UseCustomCipherSuites,
                cipherKey,
                options.CertRevocationMode,
                options.ConnectTimeout.Ticks,
                options.ReadWriteTimeout.Ticks,
                options.AutoRedirect,
                options.MaxNumberOfRedirects);
        }

        private static void CleanupSharedHttpClients(object? state)
        {
            var cutoff = DateTime.UtcNow - SharedHttpClientIdleTimeout;
            foreach (var entry in SharedHttpClients)
            {
                if (!entry.Value.IsValueCreated)
                {
                    continue;
                }

                var clientEntry = entry.Value.Value;
                if (!clientEntry.TryMarkForDisposal(cutoff))
                {
                    continue;
                }

                TryRemoveSharedHttpClient(entry.Key, entry.Value);
                clientEntry.Dispose();
            }
        }

        private static bool TryRemoveSharedHttpClient(string key, Lazy<SharedHttpClientEntry> entry)
            => ((ICollection<KeyValuePair<string, Lazy<SharedHttpClientEntry>>>)SharedHttpClients)
                .Remove(new KeyValuePair<string, Lazy<SharedHttpClientEntry>>(key, entry));

        private static HttpMessageHandler ConfigureHttpMessageHandler(HttpMessageHandler handler, HttpOptions options, CookieContainer cookieContainer)
        {
            var sslOptions = new SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = options.CertRevocationMode,
                EnabledSslProtocols = ToSslProtocols(options.SecurityProtocol) == SslProtocols.None
                        ? (SslProtocols.Tls12 | SslProtocols.Tls13)
                        : ToSslProtocols(options.SecurityProtocol),
                CipherSuitesPolicy = options.UseCustomCipherSuites && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new CipherSuitesPolicy(options.CustomCipherSuites)
                        : null,
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2, SslApplicationProtocol.Http11 },
                AllowRenegotiation = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption
            };

            if (handler is HttpClientHandler httpHandler)
            {
                httpHandler.MaxAutomaticRedirections = NormalizeMaxAutomaticRedirections(options.MaxNumberOfRedirects);
                httpHandler.AllowAutoRedirect = options.AutoRedirect;
                httpHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                httpHandler.SslProtocols = ToSslProtocols(options.SecurityProtocol);
                httpHandler.CheckCertificateRevocationList = options.CertRevocationMode == X509RevocationMode.Online;
                httpHandler.UseCookies = cookieContainer != null;
                
                if (cookieContainer != null)
                {
                    httpHandler.CookieContainer = cookieContainer;
                }

                TryApplySslOptions(httpHandler, sslOptions);
            }
            else if (handler is SocketsHttpHandler socksHandler)
            {
                socksHandler.MaxAutomaticRedirections = NormalizeMaxAutomaticRedirections(options.MaxNumberOfRedirects);
                socksHandler.AllowAutoRedirect = options.AutoRedirect;
                socksHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                socksHandler.SslOptions = sslOptions;
                socksHandler.ConnectTimeout = NormalizeHandlerTimeout(options.ConnectTimeout, nameof(options.ConnectTimeout));
                socksHandler.ResponseDrainTimeout = NormalizeHandlerTimeout(options.ReadWriteTimeout, nameof(options.ReadWriteTimeout));
                socksHandler.UseCookies = cookieContainer != null;
                
                if (cookieContainer != null)
                {
                    socksHandler.CookieContainer = cookieContainer;
                }
            }

            return handler;
        }

        private static TimeSpan NormalizeHttpClientTimeout(TimeSpan timeout)
            => timeout == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : NormalizeHandlerTimeout(timeout, nameof(timeout));

        private static void TryApplySslOptions(HttpClientHandler httpHandler, SslClientAuthenticationOptions sslOptions)
        {
            try
            {
                var field = httpHandler.GetType().GetField(
                    "_underlyingHandler",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var underlyingHandler = field?.GetValue(httpHandler);
                var sslOptionsProperty = underlyingHandler?.GetType().GetProperty(
                    "SslOptions",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (sslOptionsProperty?.CanWrite == true)
                {
                    sslOptionsProperty.SetValue(underlyingHandler, sslOptions);
                }
            }
            catch
            {
                // HttpClientHandler private implementation differs by runtime.
                // When the internal handler is unavailable, keep the public settings already applied above.
            }
        }

        private static int NormalizeMaxAutomaticRedirections(int maxRedirects)
            => maxRedirects <= 0 ? 1 : maxRedirects;

        private static TimeSpan NormalizeHandlerTimeout(TimeSpan timeout, string parameterName)
        {
            if (timeout == TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            {
                return Timeout.InfiniteTimeSpan;
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    timeout,
                    "Timeout must be zero or greater, or Timeout.InfiniteTimeSpan to disable the timeout.");
            }

            return timeout;
        }

        /// <summary>
        /// Converts the <paramref name="protocol"/> to an SslProtocols enum. Multiple protocols are not supported and SystemDefault is None.
        /// </summary>
        private static SslProtocols ToSslProtocols(SecurityProtocol protocol)
        {


            return protocol switch
            {
                SecurityProtocol.SystemDefault => SslProtocols.None,
                SecurityProtocol.TLS10 => SslProtocols.Tls,
                SecurityProtocol.TLS11 => SslProtocols.Tls11,
                SecurityProtocol.TLS12 => SslProtocols.Tls12,
                SecurityProtocol.TLS13 => SslProtocols.Tls13,
                _ => throw new Exception("Protocol not supported"),
            };
        }
    }
}
