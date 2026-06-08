using RuriLib.Functions.Http.Options;
using RuriLib.Functions.Files;
using RuriLib.Extensions;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal abstract class HttpRequestHandler
    {
        public virtual Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
            => throw new NotImplementedException();
        public virtual Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
            => throw new NotImplementedException();

        protected static StreamContent CreateFileContent(Stream stream, string fieldName, string fileName, string contentType)
        {
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = $"\"{fieldName}\"",
                FileName = $"\"{fileName}\""
            };
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return fileContent;
        }

        protected static TlsCipherSuite[] ParseCipherSuites(List<string> cipherSuites)
            => HttpRequestNormalizer.ParseCipherSuites(cipherSuites);

        protected static HttpOptions GetClientOptions(BotData data, Options.HttpRequestOptions options)
            => HttpRequestNormalizer.GetClientOptions(data, options);

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, StandardHttpRequestOptions options)
            => HttpRequestNormalizer.Create(data, options);

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, RawHttpRequestOptions options)
            => HttpRequestNormalizer.Create(data, options);

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, BasicAuthHttpRequestOptions options)
            => HttpRequestNormalizer.Create(data, options);

        protected static NormalizedHttpRequest CreateNormalizedRequest(BotData data, MultipartHttpRequestOptions options)
            => HttpRequestNormalizer.Create(data, options);

        protected async Task ExecutePipelineAsync(
            BotData data,
            NormalizedHttpRequest request,
            string transportName,
            Func<NormalizedHttpRequest, CancellationToken, Task<NormalizedHttpResponse>> sendAsync)
        {
            HttpRequestNormalizer.Validate(request);

            while (true)
            {
                data.Logger.LogHeader();
                HttpPipelineLogger.LogRequest(data, request);

                try
                {
                    Activity.Current = null;
                    using var linkedCts = CreateLinkedTimeoutTokenSource(data.CancellationToken, request.TimeoutMilliseconds);

                    var response = await sendAsync(request, linkedCts.Token).ConfigureAwait(false);
                    HttpResponseMapper.Apply(data, response, request);

                    if (!HttpRedirectPolicy.TryCreateRedirectRequest(request, response, out var redirectRequest))
                    {
                        return;
                    }

                    request = redirectRequest;
                }
                catch (Exception ex)
                {
                    HttpPipelineLogger.LogException(data, request, ex, transportName);
                    throw;
                }
            }
        }

        private static CancellationTokenSource CreateLinkedTimeoutTokenSource(CancellationToken parentToken, int timeoutMilliseconds)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            if (timeoutMilliseconds > 0)
            {
                linked.CancelAfter(timeoutMilliseconds);
            }

            return linked;
        }

        protected static NormalizedHttpResponse CreateResponseSnapshot(
            Uri address,
            int statusCode,
            Dictionary<string, List<string>> headers,
            byte[] body)
            => new()
            {
                Address = address,
                StatusCode = statusCode,
                Headers = headers,
                RawBody = body ?? Array.Empty<byte>()
            };

        protected static Dictionary<string, List<string>> NormalizeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (!normalized.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    normalized[header.Key] = values;
                }

                foreach (var value in header.Value ?? Array.Empty<string>())
                {
                    values.Add(value);
                }
            }

            return normalized;
        }

        protected static Dictionary<string, List<string>> NormalizeHeaders(Dictionary<string, List<string>> headers)
        {
            // Already in the target format — return as-is (case-insensitive comparer already set)
            return headers;
        }

        protected static Dictionary<string, List<string>> NormalizeHeaders(IEnumerable<KeyValuePair<string, string>> headers)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                if (!normalized.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    normalized[header.Key] = values;
                }

                values.Add(header.Value);
            }

            return normalized;
        }

        protected static HttpContent? CreateHttpContent(BotData data, NormalizedHttpRequest request, out FileStream? fileStream)
        {
            fileStream = null;

            if (request.MultipartContents != null)
            {
                var multipartContent = new MultipartFormDataContent(request.Boundary);
                multipartContent.Headers.ContentType.Parameters.First(o => o.Name == "boundary").Value = request.Boundary;

                foreach (var content in request.MultipartContents)
                {
                    switch (content)
                    {
                        case StringHttpContent stringContent:
                            multipartContent.Add(new StringContent(stringContent.Data, Encoding.UTF8, stringContent.ContentType), stringContent.Name);
                            break;

                        case RawHttpContent rawContent:
                            var byteContent = new ByteArrayContent(rawContent.Data);
                            byteContent.Headers.ContentType = new MediaTypeHeaderValue(rawContent.ContentType);
                            multipartContent.Add(byteContent, rawContent.Name);
                            break;

                        case FileHttpContent fileContent:
                            lock (FileLocker.GetHandle(fileContent.FileName))
                            {
                                if (data.Providers.Security.RestrictBlocksToCWD)
                                {
                                    FileUtils.ThrowIfNotInCWD(fileContent.FileName);
                                }

                                fileStream = new FileStream(fileContent.FileName, FileMode.Open);
                                multipartContent.Add(
                                    CreateFileContent(fileStream, fileContent.Name, Path.GetFileName(fileContent.FileName), fileContent.ContentType),
                                    fileContent.Name);
                            }
                            break;
                    }
                }

                return multipartContent;
            }

            if (request.RawBody != null)
            {
                var rawContent = new ByteArrayContent(request.RawBody);
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                {
                    rawContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
                }

                return rawContent;
            }

            if (request.StringBody == null && string.IsNullOrWhiteSpace(request.ContentType))
            {
                return null;
            }

            var stringContentValue = request.StringBody?.Unescape() ?? string.Empty;
            var textContent = new StringContent(stringContentValue);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                textContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }

            return textContent;
        }

        protected static Dictionary<string, string> CopyHeaders(NormalizedHttpRequest request)
            => HttpRequestNormalizer.NormalizeSingleValueHeaders(request.Headers);

        protected static Dictionary<string, string> CopyCookies(NormalizedHttpRequest request)
            => request.Cookies.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }
}
