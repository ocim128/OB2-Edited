using RuriLib.Extensions;
using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Http;
using RuriLib.Http.Models;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Functions.Conversion;

namespace RuriLib.Functions.Http
{
    internal class RLHttpClientRequestHandler : HttpRequestHandler
    {
        public async override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            using var client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions);

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine
            };

            if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
            {
                var content = options.Content;

                if (options.UrlEncodeContent)
                {
                    content = string.Join("", content.SplitInChunks(2080)
                        .Select(Uri.EscapeDataString))
                        .Replace($"%26", "&").Replace($"%3D", "=");
                }

                request.Content = new StringContent(content.Unescape());
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);
            }

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, request);
                throw;
            }
        }

        public async override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            using var client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions);

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine,
                Content = new ByteArrayContent(options.Content)
            };

            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, request);
                throw;
            }
        }

        public async override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            var clientOptions = GetClientOptions(data, options);
            using var client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions);

            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            using var request = new HttpRequest
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                Uri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Headers = options.CustomHeaders,
                Cookies = data.COOKIES,
                AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine
            };

            // Add the basic auth header
            request.AddHeader("Authorization", "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, request);
                throw;
            }
        }

        public async override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
                var clientOptions = GetClientOptions(data, options);
                using var client = HttpFactory.GetRLHttpClient(data.UseProxy ? data.Proxy : null, clientOptions);

                foreach (var cookie in options.CustomCookies)
                    data.COOKIES[cookie.Key] = cookie.Value;

                if (string.IsNullOrWhiteSpace(options.Boundary))
                    options.Boundary = GenerateMultipartBoundary();

                // Rewrite the value of the Content-Type header otherwise it will add double quotes around it like
                // Content-Type: multipart/form-data; boundary="------WebKitFormBoundaryewozmkbxwbblilpm"
                var multipartContent = new MultipartFormDataContent(options.Boundary);
                multipartContent.Headers.ContentType.Parameters.First(o => o.Name == "boundary").Value = options.Boundary;

                FileStream fileStream = null;

                foreach (var c in options.Contents)
                {
                    switch (c)
                    {
                        case StringHttpContent x:
                            multipartContent.Add(new StringContent(x.Data, Encoding.UTF8, x.ContentType), x.Name);
                            break;

                        case RawHttpContent x:
                            var byteContent = new ByteArrayContent(x.Data);
                            byteContent.Headers.ContentType = new MediaTypeHeaderValue(x.ContentType);
                            multipartContent.Add(byteContent, x.Name);
                            break;

                        case FileHttpContent x:
                            lock (FileLocker.GetHandle(x.FileName))
                            {
                                if (data.Providers.Security.RestrictBlocksToCWD)
                                    FileUtils.ThrowIfNotInCWD(x.FileName);

                                fileStream = new FileStream(x.FileName, FileMode.Open);
                                var fileContent = CreateFileContent(fileStream, x.Name, Path.GetFileName(x.FileName), x.ContentType);
                                multipartContent.Add(fileContent, x.Name);
                            }
                            break;
                    }
                }

                using var request = new HttpRequest
                {
                    Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                    Uri = new Uri(options.Url),
                    Version = Version.Parse(options.HttpVersion),
                    Headers = options.CustomHeaders,
                    Cookies = data.COOKIES,
                    AbsoluteUriInFirstLine = options.AbsoluteUriInFirstLine,
                    Content = multipartContent
                };

                data.Logger.LogHeader();

                try
                {
                    Activity.Current = null;
                    using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                    using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                    LogHttpRequestData(data, client);
                    await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
                }
                catch
                {
                    LogHttpRequestData(data, request, options.Boundary, options.Contents);
                    throw;
                }
                finally
                {
                    if (fileStream != null)
                        await fileStream.DisposeAsync().ConfigureAwait(false);
                }
        }

        private static void LogHttpRequestData(BotData data, HttpRequest request,
            string boundary = null, List<MyHttpContent> multipartContents = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{request.Method.Method} {request.Uri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

            // Log the headers
            if (!request.HeaderExists("Host", out _))
                sb.AppendLine($"Host: {request.Uri.Host}");

            foreach (var header in request.Headers)
            {
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            // Log the cookie header
            var cookies = request.Cookies.Select(c => $"{c.Key}={c.Value}");

            if (cookies.Any())
                sb.AppendLine($"Cookie: {string.Join("; ", cookies)}");

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                if (request.Content is StringContent stringContent)
                {
                    sb.AppendLine();
                    sb.AppendLine(stringContent.ReadAsStringAsync().Result);
                }
                else if (request.Content is ByteArrayContent byteArrayContent)
                {
                    sb.AppendLine();
                    sb.AppendLine(Base64Converter.ToBase64String(byteArrayContent.ReadAsByteArrayAsync().Result));
                }
                else if (request.Content is MultipartFormDataContent)
                {
                    sb.AppendLine();
                    sb.AppendLine(SerializeMultipart(boundary, multipartContents));
                }
            }

            data.Logger.Log(sb.ToString(), LogColors.Azure);
        }

        private static void LogHttpRequestData(BotData data, RLHttpClient client)
        {
            for (var i = 0; i < client.RawRequests.Count; i++)
            {
                if (i > 0)
                {
                    data.Logger.Log($"Redirect {i}", LogColors.Beige);
                }

                data.Logger.Log(Encoding.UTF8.GetString(client.RawRequests[i]), LogColors.NonPhotoBlue);
            }
        }

        private static async Task LogHttpResponseData(BotData data, HttpResponse response, HttpRequest request,
            RuriLib.Functions.Http.Options.HttpRequestOptions requestOptions)
        {
            // Try to read the raw source for Content-Length calculation
            try
            {
                data.RAWSOURCE = await response.Content.ReadAsByteArrayAsync(data.CancellationToken).ConfigureAwait(false);
            }
            catch (NullReferenceException)
            {
                // Thrown when there is no content (204) or we decided to not read it
                data.RAWSOURCE = Array.Empty<byte>();
            }

            // Address
            var uri = response.Request.Uri;
            if (!uri.IsAbsoluteUri)
                uri = new Uri(request.Uri, uri);
            data.ADDRESS = response.Request.Uri.AbsoluteUri;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

            // Response code
            data.RESPONSECODE = (int)response.StatusCode;
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            // Headers
            var sbHeaders = new StringBuilder();
            sbHeaders.AppendLine("Received Headers:");
            data.HEADERS = response.Headers;
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    data.HEADERS[header.Key] = string.Join(", ", header.Value);
                    sbHeaders.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
            }

            foreach (var header in response.Headers)
            {
                sbHeaders.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            if (!data.HEADERS.ContainsKey("Content-Length"))
                data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();

            data.Logger.Log(sbHeaders.ToString(), LogColors.Violet);

            // Cookies
            var sbCookies = new StringBuilder();
            sbCookies.AppendLine("Received Cookies:");
            foreach (var cookie in data.COOKIES)
            {
                sbCookies.AppendLine($"{cookie.Key}: {cookie.Value}");
            }
            data.Logger.Log(sbCookies.ToString(), LogColors.Khaki);

            // Unzip the GZipped content if still gzipped (after Content-Length calculation)
            if (data.RAWSOURCE.Length > 1 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B)
            {
                try
                {
                    data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
                }
                catch
                {
                    data.Logger.Log("Tried to unzip but failed", LogColors.DarkOrange);
                }
            }

            // Source
            if (!string.IsNullOrWhiteSpace(requestOptions.CodePagesEncoding))
            {
                data.SOURCE = CodePagesEncodingProvider.Instance
                    .GetEncoding(requestOptions.CodePagesEncoding).GetString(data.RAWSOURCE);
            }
            else
            {
                data.SOURCE = Encoding.UTF8.GetString(data.RAWSOURCE);
            }
            
            if (requestOptions.DecodeHtml)
            {
                data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
            }

            data.Logger.Log("Received Payload:", LogColors.ForestGreen);
            data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
        }
    }
}
