using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text;

namespace RuriLib.Functions.Http
{
    internal class WinHttpClientRequestHandler : HttpRequestHandler
    {
        private static readonly Dictionary<string, HttpClient> _clients = new();

        public async override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            var client = GetOrCreateClient(data, options);
            using var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(options.Method.ToString()), options.Url);

            foreach (var header in options.CustomHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (!string.IsNullOrEmpty(options.Content))
            {
                request.Content = new StringContent(options.Content);
                request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(options.ContentType);
            }

            var response = await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public async override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            var client = GetOrCreateClient(data, options);
            using var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(options.Method.ToString()), options.Url);

            foreach (var header in options.CustomHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            request.Content = new ByteArrayContent(options.Content);
            request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(options.ContentType);

            var response = await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public async override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            var client = GetOrCreateClient(data, options);
            using var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(options.Method.ToString()), options.Url);

            foreach (var header in options.CustomHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", 
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

            var response = await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options) => throw new NotImplementedException();

        private HttpClient GetOrCreateClient(BotData data, RuriLib.Functions.Http.Options.HttpRequestOptions options)
        {
            var key = $"{data.UseProxy}:{data.Proxy?.Host}:{data.Proxy?.Port}";
            if (!_clients.TryGetValue(key, out var client))
            {
                var handler = new WinHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    AutomaticRedirection = options.AutoRedirect,
                    MaxAutomaticRedirections = options.MaxNumberOfRedirects
                };

                if (data.UseProxy)
                {
                    handler.Proxy = new WebProxy(data.Proxy.Host, data.Proxy.Port);
                    if (!string.IsNullOrEmpty(data.Proxy.Username))
                    {
                        handler.Proxy.Credentials = new NetworkCredential(data.Proxy.Username, data.Proxy.Password);
                    }
                }

                client = new HttpClient(handler);
                _clients[key] = client;
            }
            return client;
        }

        private async Task LogResponse(BotData data, HttpResponseMessage response, RuriLib.Functions.Http.Options.HttpRequestOptions options)
        {
            data.RESPONSECODE = (int)response.StatusCode;
            data.ADDRESS = response.RequestMessage.RequestUri.AbsoluteUri;
            data.RAWSOURCE = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            data.HEADERS.Clear();
            foreach (var header in response.Headers)
                data.HEADERS[header.Key] = string.Join(", ", header.Value);

            foreach (var header in response.Content.Headers)
                data.HEADERS[header.Key] = string.Join(", ", header.Value);

            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);
            
            if (data.RAWSOURCE.Length > 0)
            {
                data.Logger.Log($"Received {data.RAWSOURCE.Length} bytes", LogColors.White);
            }
        }
    }
}
