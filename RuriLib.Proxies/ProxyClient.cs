using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System;
using RuriLib.Proxies.Exceptions;
using RuriLib.Proxies.Clients;
using System.Security;

namespace RuriLib.Proxies
{
    /// <summary>
    /// Can produce proxied <see cref="TcpClient"/> instances.
    /// </summary>
    public abstract class ProxyClient
    {
        /// <summary>
        /// The proxy settings.
        /// </summary>
        public ProxySettings Settings { get; }

        /// <summary>
        /// Instantiates a proxy client with the given <paramref name="settings"/>.
        /// </summary>
        public ProxyClient(ProxySettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// /// <summary>
        /// Create a proxied <see cref="TcpClient"/> to the destination host.
        /// </summary>
        /// <param name="destinationHost">The host you want to connect to</param>
        /// <param name="destinationPort">The port on which the host is listening</param>
        /// <param name="cancellationToken">A token to cancel the connection attempt</param>
        /// <param name="tcpClient">A <see cref="TcpClient"/> instance (if null, a new one will be created)</param>
        /// <exception cref="ArgumentException">Value of <paramref name="destinationHost"/> is <see langword="null"/> or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Value of <paramref name="destinationPort"/> less than 1 or greater than 65535.</exception>
        /// <exception cref="ProxyException">Error while working with the proxy.</exception>
        public async Task<TcpClient> ConnectAsync(string destinationHost, int destinationPort, TcpClient tcpClient = null,
            CancellationToken cancellationToken = default)
        {
            var client = tcpClient ?? new TcpClient();
            ApplySocketTimeouts(client);

            var host = Settings.Host;
            var port = Settings.Port;

            // NoProxy case, connect directly to the server without proxy
            if (this is NoProxyClient)
            {
                host = destinationHost;
                port = destinationPort;
            }

            // Try to connect to the proxy (or directly to the server in the NoProxy case)
            try
            {
                using var connectCts = CreateConnectCancellationTokenSource(cancellationToken);
                await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);

                await CreateConnectionAsync(client, destinationHost, destinationPort, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                client.Close();

                if (ex is SocketException or SecurityException)
                {
                    throw new ProxyException($"Failed to connect to {(this is NoProxyClient ? "server" : "proxy-server")}", ex);
                }

                throw;
            }

            return client;
        }

        private void ApplySocketTimeouts(TcpClient client)
        {
            var timeoutMilliseconds = NormalizeSocketTimeoutMilliseconds(Settings.ReadWriteTimeOut, nameof(Settings.ReadWriteTimeOut));

            if (!timeoutMilliseconds.HasValue)
            {
                return;
            }

            client.ReceiveTimeout = timeoutMilliseconds.Value;
            client.SendTimeout = timeoutMilliseconds.Value;
        }

        private CancellationTokenSource CreateConnectCancellationTokenSource(CancellationToken cancellationToken)
        {
            var timeout = Settings.ConnectTimeout;
            if (timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.Zero)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Settings.ConnectTimeout),
                    timeout,
                    "ConnectTimeout must be zero or greater, or Timeout.InfiniteTimeSpan to disable the timeout.");
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);
            return linkedCts;
        }

        private static int? NormalizeSocketTimeoutMilliseconds(TimeSpan timeout, string parameterName)
        {
            if (timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.Zero)
            {
                return null;
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    timeout,
                    "Socket timeout must be zero or greater, or Timeout.InfiniteTimeSpan to disable the timeout.");
            }

            var timeoutMilliseconds = timeout.TotalMilliseconds;
            if (timeoutMilliseconds > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)timeoutMilliseconds;
        }

        /// <summary>
        /// Proxy protocol specific connection.
        /// </summary>
        /// <param name="tcpClient">The <see cref="TcpClient"/> that can be used to connect to the proxy over TCP</param>
        /// <param name="destinationHost">The target host that the proxy needs to connect to</param>
        /// <param name="destinationPort">The target port that the proxy needs to connect to</param>
        /// <param name="cancellationToken">A token to cancel operations</param>
        protected virtual Task CreateConnectionAsync(TcpClient tcpClient, string destinationHost, int destinationPort,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
