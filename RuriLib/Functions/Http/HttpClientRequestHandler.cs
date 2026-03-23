using RuriLib.Functions.Http.Options;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal class HttpClientRequestHandler : HttpRequestHandler
    {
        private static readonly HttpClient sharedClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseCookies = false
        });

        public override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
            => ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "SystemNet",
                (request, token) => SendAsync(data, request, token));

        public override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
            => ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "SystemNet",
                (request, token) => SendAsync(data, request, token));

        public override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
            => ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "SystemNet",
                (request, token) => SendAsync(data, request, token));

        public override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
            => ExecutePipelineAsync(
                data,
                CreateNormalizedRequest(data, options),
                "SystemNet",
                (request, token) => SendAsync(data, request, token));

        private static async Task<NormalizedHttpResponse> SendAsync(
            BotData data,
            NormalizedHttpRequest request,
            CancellationToken cancellationToken)
        {
            FileStream? fileStream = null;
            using var content = CreateHttpContent(data, request, out fileStream);
            using var message = new HttpRequestMessage
            {
                Method = request.Method,
                RequestUri = request.Uri,
                Version = request.Version,
                Content = content
            };

            foreach (var header in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var cookieHeader = string.Join("; ",
                request.Cookies.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            try
            {
                using var response = await sharedClient.SendAsync(
                    message,
                    request.ReadResponseContent ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                var statusCode = (int)response.StatusCode;
                var body = request.ReadResponseContent && (statusCode < 300 || statusCode >= 400)
                    ? await ReadResponseBodyAsync(response, cancellationToken).ConfigureAwait(false)
                    : Array.Empty<byte>();
                var headers = NormalizeHeaders(response.Headers.Concat(response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()));
                var address = response.RequestMessage?.RequestUri ?? request.Uri;

                return CreateResponseSnapshot(address, statusCode, headers, body);
            }
            finally
            {
                if (fileStream != null)
                {
                    await fileStream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task<byte[]> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NullReferenceException)
            {
                return Array.Empty<byte>();
            }
        }
    }
}
