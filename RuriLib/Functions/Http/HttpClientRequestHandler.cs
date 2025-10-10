using RuriLib.Extensions;
using RuriLib.Functions.Conversion;
using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal class HttpClientRequestHandler : HttpRequestHandler
    {
        private static readonly HttpClient sharedClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseCookies = false
        });

        // Fast-path check for common network exceptions to avoid full recursive check
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLikelyNetworkException(Exception ex)
        {
            if (ex == null)
                return false;

            // Fast path for most common network exceptions (single type check)
            // Use type comparison for better performance in hot paths
            var exType = ex.GetType();
            if (exType == typeof(HttpRequestException) ||
                exType == typeof(WebException) ||
                exType == typeof(SocketException) ||
                exType == typeof(TimeoutException))
                return true;

            // Only do the full check for less common exceptions
            if (exType == typeof(OperationCanceledException) || exType == typeof(IOException))
                return NetworkExceptionHelper.IsNetworkException(ex);

            return false;
        }

        public async override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var cookieContainer = new CookieContainer();

            foreach (var cookie in data.COOKIES)
            {
                cookieContainer.Add(new Uri(options.Url), new Cookie(cookie.Key, cookie.Value));
            }

            // Merge any raw Cookie headers from CustomHeaders into the cookie jar, filtering out empty values
            MergeCookieHeader(options.CustomHeaders, data.COOKIES);

            var clientOptions = GetClientOptions(data, options);
            var client = sharedClient;
            using var request = new HttpRequestMessage
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                RequestUri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion)
            };

            foreach (var header in options.CustomHeaders)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                    continue; // prevent sending raw Cookie headers; we build a filtered one below
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Manually add cookies as Cookie header since UseCookies = false
            if (data.COOKIES.Count > 0)
            {
                var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
            }

            string content = null;

            if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
            {
                content = options.Content;

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
            LogHttpRequestData(data, request, content);

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);

                var response = await client.SendAsync(request, options.ReadResponseContent ?
                    HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                // Fast redirect handling if auto redirect is enabled
                int redirectCount = 0;
                while (options.AutoRedirect && redirectCount < options.MaxNumberOfRedirects &&
                       ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) &&
                       response.Headers.Location != null)
                {
                    // Before following redirect, log response to update data.COOKIES
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);

                    var location = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(new Uri(options.Url), response.Headers.Location);

                    response.Dispose();

                    // Create redirect request with minimal headers
                    using var redirectRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, location)
                    {
                        Version = request.Version
                    };

                    // Copy essential headers only
                    if (request.Headers.UserAgent.Count > 0)
                        redirectRequest.Headers.UserAgent.ParseAdd(request.Headers.UserAgent.ToString());

                    // Re-add accumulated cookies for the redirect
                    if (data.COOKIES.Count > 0)
                    {
                        var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                        if (!string.IsNullOrEmpty(cookieHeader))
                        {
                            redirectRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                        }
                    }

                    response = await client.SendAsync(redirectRequest, options.ReadResponseContent ?
                        HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                        linkedCts.Token).ConfigureAwait(false);

                    redirectCount++;
                }

                using (response)
                {
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                LogHttpRequestData(data, request, content);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
        }

        public async override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var cookieContainer = new CookieContainer();

            foreach (var cookie in data.COOKIES)
            {
                cookieContainer.Add(new Uri(options.Url), new Cookie(cookie.Key, cookie.Value));
            }

            // Merge any raw Cookie headers from CustomHeaders into the cookie jar, filtering out empty values
            MergeCookieHeader(options.CustomHeaders, data.COOKIES);

            var clientOptions = GetClientOptions(data, options);
            var client = sharedClient;
            using var request = new HttpRequestMessage
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                RequestUri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Content = new ByteArrayContent(options.Content)
            };

            foreach (var header in options.CustomHeaders)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                    continue; // prevent sending raw Cookie headers; we build a filtered one below
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Manually add cookies as Cookie header since UseCookies = false
            if (data.COOKIES.Count > 0)
            {
                var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
            }

            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);

            data.Logger.LogHeader();
            LogHttpRequestData(data, request, Base64Converter.ToBase64String(options.Content));

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);

                var response = await client.SendAsync(request, options.ReadResponseContent ?
                    HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                // Fast redirect handling (but usually not used for raw requests)
                int redirectCount = 0;
                while (options.AutoRedirect && redirectCount < options.MaxNumberOfRedirects &&
                       ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) &&
                       response.Headers.Location != null)
                {
                    // Before following redirect, log response to update data.COOKIES
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);

                    var location = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(new Uri(options.Url), response.Headers.Location);

                    response.Dispose();

                    using var redirectRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, location)
                    {
                        Version = request.Version
                    };

                    if (request.Headers.UserAgent.Count > 0)
                        redirectRequest.Headers.UserAgent.ParseAdd(request.Headers.UserAgent.ToString());

                    // Re-add accumulated cookies for the redirect
                    if (data.COOKIES.Count > 0)
                    {
                        var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                        if (!string.IsNullOrEmpty(cookieHeader))
                        {
                            redirectRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                        }
                    }

                    response = await client.SendAsync(redirectRequest, options.ReadResponseContent ?
                        HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                        linkedCts.Token).ConfigureAwait(false);

                    redirectCount++;
                }

                using (response)
                {
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                LogHttpRequestData(data, request, Base64Converter.ToBase64String(options.Content));
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
        }

        public async override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var cookieContainer = new CookieContainer();

            foreach (var cookie in data.COOKIES)
            {
                cookieContainer.Add(new Uri(options.Url), new Cookie(cookie.Key, cookie.Value));
            }

            // Merge any raw Cookie headers from CustomHeaders into the cookie jar, filtering out empty values
            MergeCookieHeader(options.CustomHeaders, data.COOKIES);

            var clientOptions = GetClientOptions(data, options);
            var client = sharedClient;
            using var request = new HttpRequestMessage
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                RequestUri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion)
            };

            foreach (var header in options.CustomHeaders)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                    continue; // prevent sending raw Cookie headers; we build a filtered one below
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Manually add cookies as Cookie header since UseCookies = false
            if (data.COOKIES.Count > 0)
            {
                var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
            }

            // Add the basic auth header
            request.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

            data.Logger.LogHeader();
            LogHttpRequestData(data, request);

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);

                var response = await client.SendAsync(request, options.ReadResponseContent ?
                    HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                // Fast redirect handling for basic auth
                int redirectCount = 0;
                while (options.AutoRedirect && redirectCount < options.MaxNumberOfRedirects &&
                       ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) &&
                       response.Headers.Location != null)
                {
                    // Before following redirect, log response to update data.COOKIES
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);

                    var location = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(new Uri(options.Url), response.Headers.Location);

                    response.Dispose();

                    using var redirectRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, location)
                    {
                        Version = request.Version
                    };

                    if (request.Headers.UserAgent.Count > 0)
                        redirectRequest.Headers.UserAgent.ParseAdd(request.Headers.UserAgent.ToString());

                    // Keep basic auth for redirects to same domain
                    if (location.Host.Equals(new Uri(options.Url).Host, StringComparison.OrdinalIgnoreCase))
                    {
                        redirectRequest.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(
                            Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));
                    }

                    // Re-add accumulated cookies for the redirect
                    if (data.COOKIES.Count > 0)
                    {
                        var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                        if (!string.IsNullOrEmpty(cookieHeader))
                        {
                            redirectRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                        }
                    }

                    response = await client.SendAsync(redirectRequest, options.ReadResponseContent ?
                        HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                        linkedCts.Token).ConfigureAwait(false);

                    redirectCount++;
                }

                using (response)
                {
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                LogHttpRequestData(data, request);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
        }
        public async override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            var cookieContainer = new CookieContainer();

            foreach (var cookie in data.COOKIES)
            {
                cookieContainer.Add(new Uri(options.Url), new Cookie(cookie.Key, cookie.Value));
            }

            var clientOptions = GetClientOptions(data, options);
            var client = sharedClient;
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

            using var request = new HttpRequestMessage
            {
                Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                RequestUri = new Uri(options.Url),
                Version = Version.Parse(options.HttpVersion),
                Content = multipartContent
            };

            // Merge any raw Cookie headers from CustomHeaders into the cookie jar, filtering out empty values
            MergeCookieHeader(options.CustomHeaders, data.COOKIES);

            foreach (var header in options.CustomHeaders)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                    continue; // prevent sending raw Cookie headers; we build a filtered one below
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Manually add cookies as Cookie header since UseCookies = false
            if (data.COOKIES.Count > 0)
            {
                var cookieHeader = string.Join("; ", data.COOKIES.Where(c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
            }

            data.Logger.LogHeader();
            LogHttpRequestData(data, request, SerializeMultipart(options.Boundary, options.Contents), options.Boundary);

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);

                var response = await client.SendAsync(request, options.ReadResponseContent ?
                    HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);

                // Fast redirect handling for multipart (usually not needed but supported)
                int redirectCount = 0;
                while (options.AutoRedirect && redirectCount < options.MaxNumberOfRedirects &&
                       ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) &&
                       response.Headers.Location != null)
                {
                    // Before following redirect, log response to update data.COOKIES
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);

                    var location = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(new Uri(options.Url), response.Headers.Location);

                    response.Dispose();

                    using var redirectRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, location)
                    {
                        Version = request.Version
                    };

                    if (request.Headers.UserAgent.Count > 0)
                        redirectRequest.Headers.UserAgent.ParseAdd(request.Headers.UserAgent.ToString());

                    // Re-add accumulated cookies for the redirect
                    if (data.COOKIES.Count > 0)
                    {
                        var cookieHeader = string.Join("; ", data.COOKIES.Where(static c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}"));
                        if (!string.IsNullOrEmpty(cookieHeader))
                        {
                            redirectRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                        }
                    }

                    response = await client.SendAsync(redirectRequest, options.ReadResponseContent ?
                        HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                        linkedCts.Token).ConfigureAwait(false);

                    redirectCount++;
                }

                using (response)
                {
                    await LogHttpResponseData(data, response, cookieContainer, options).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsLikelyNetworkException(ex))
            {
                LogHttpRequestData(data, request, SerializeMultipart(options.Boundary, options.Contents), options.Boundary);
                data.Logger.Log($"Network exception detected: {ex.GetType().Name} - {ex.Message}", LogColors.Orange);
                throw; // Re-throw to be caught by the retry logic
            }
            finally
            {
                if (fileStream != null)
                    await fileStream.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static void MergeCookieHeader(IDictionary<string, string> headers, IDictionary<string, string> cookieJar)
        {
            if (headers == null || headers.Count == 0) return;

            foreach (var header in headers)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("cookies", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = header.Value;
                    if (string.IsNullOrEmpty(raw)) continue;

                    foreach (var part in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length == 0) continue;
                        var idx = trimmed.IndexOf('=');
                        if (idx <= 0) continue;

                        var name = trimmed.Substring(0, idx).Trim();
                        var value = trimmed.Substring(idx + 1);

                        if (!string.IsNullOrEmpty(value))
                        {
                            cookieJar[name] = value;
                        }
                    }
                }
            }
        }

        private static void LogHttpRequestData(BotData data, HttpRequestMessage request, string content = null, string boundary = null)
        {
            using var writer = new StringWriter();

            // Log the method, uri and http version
            writer.WriteLine($"{request.Method.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

            // Log the headers
            writer.WriteLine($"Host: {request.RequestUri.Host}");

            foreach (var header in request.Headers)
            {
                var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
                writer.WriteLine($"{header.Key}: {string.Join(separator, header.Value)}");
            }

            // Log the cookie header
            var cookies = data.COOKIES.Where(c => !string.IsNullOrEmpty(c.Value)).Select(c => $"{c.Key}={c.Value}");

            if (cookies.Any())
                writer.WriteLine($"Cookie: {string.Join("; ", cookies)}");

            if (request.Content != null && content != null)
            {
                switch (request.Content)
                {
                    case StringContent x:
                        writer.WriteLine($"Content-Type: {x.Headers.ContentType}");
                        writer.WriteLine($"Content-Length: {x.Headers.ContentLength}");
                        writer.WriteLine();
                        writer.WriteLine(content);
                        break;

                    case ByteArrayContent x:
                        writer.WriteLine($"Content-Type: {x.Headers.ContentType}");
                        writer.WriteLine($"Content-Length: {x.Headers.ContentLength}");
                        writer.WriteLine();
                        writer.WriteLine(content);
                        break;

                    case MultipartFormDataContent x:
                        writer.WriteLine($"Content-Type: multipart/form-data; boundary=\"{boundary}\"");
                        writer.WriteLine($"Content-Length: (not calculated)");
                        writer.WriteLine();
                        writer.WriteLine(content);
                        break;
                }
            }

            data.Logger.Log(writer.ToString(), LogColors.NonPhotoBlue);
        }

        private static async Task LogHttpResponseData(BotData data, HttpResponseMessage response,
            CookieContainer cookieContainer, Options.HttpRequestOptions requestOptions)
        {
            // Skip reading payload on redirects and read content only if requested
            int status = (int)response.StatusCode;
            if (requestOptions.ReadResponseContent && (status < 300 || status >= 400))
            {
                try
                {
                    data.RAWSOURCE = await response.Content.ReadAsByteArrayAsync(data.CancellationToken).ConfigureAwait(false);
                }
                catch (NullReferenceException)
                {
                    data.RAWSOURCE = Array.Empty<byte>();
                }
            }
            else
            {
                data.RAWSOURCE = Array.Empty<byte>();
            }

            // Address
            data.ADDRESS = response.RequestMessage.RequestUri.AbsoluteUri;
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

            // Response code
            data.RESPONSECODE = (int)response.StatusCode;
            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

            // Headers (conditional parsing for speed)
            if (!requestOptions.DisableHeaderParsing)
            {
                static string GetHeaderValue(KeyValuePair<string, IEnumerable<string>> header)
                {
                    // For Set-Cookie headers, show only the name=value part
                    if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanedCookies = new List<string>();
                        foreach (var cookieHeader in header.Value)
                        {
                            if (!string.IsNullOrEmpty(cookieHeader))
                            {
                                var separatorPos = cookieHeader.IndexOf('=');
                                if (separatorPos > 0)
                                {
                                    var cookieName = cookieHeader.AsSpan(0, separatorPos).ToString();
                                    var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
                                    var cookieValue = endCookiePos == -1
                                        ? cookieHeader.AsSpan(separatorPos + 1).ToString()
                                        : cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString();
                                    cleanedCookies.Add($"{cookieName}={cookieValue}");
                                }
                            }
                        }
                        return string.Join(", ", cleanedCookies);
                    }
                    else
                    {
                        var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
                        return string.Join(separator, header.Value);
                    }
                }

                data.HEADERS = response.Headers.Concat(response.Content.Headers)
                                    .ToDictionary(h => h.Key, h => GetHeaderValue(h));

                if (!data.HEADERS.ContainsKey("Content-Length"))
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();

                data.Logger.Log("Received Headers:", LogColors.MediumPurple);
                data.Logger.Log(data.HEADERS.Select(h => $"{h.Key}: {h.Value}"), LogColors.Violet);
            }
            else
            {
                data.HEADERS.Clear();
                data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
                data.Logger.Log("Header Parsing Skipped", LogColors.Orange);
            }

            // Cookies (conditional parsing for speed)
            if (!requestOptions.DisableCookieParsing)
            {
                // Since UseCookies = false, we need to manually parse Set-Cookie headers
                // The cookieContainer approach won't work with UseCookies = false

                static bool TryParseCookie(string cookieHeader, out string cookieName, out string cookieValue)
                {
                    cookieName = null;
                    cookieValue = null;

                    if (string.IsNullOrEmpty(cookieHeader))
                    {
                        return false;
                    }

                    var separatorPos = cookieHeader.IndexOf('=');
                    if (separatorPos <= 0)
                    {
                        // Invalid cookie, don't add it
                        return false;
                    }

                    cookieName = cookieHeader.AsSpan(0, separatorPos).ToString();

                    var endCookiePos = cookieHeader.IndexOf(';', separatorPos);
                    if (endCookiePos == -1)
                    {
                        cookieValue = cookieHeader.AsSpan(separatorPos + 1).ToString();
                    }
                    else
                    {
                        cookieValue = cookieHeader.AsSpan(separatorPos + 1, endCookiePos - separatorPos - 1).ToString();
                    }

                    return true;
                }

                // Parse Set-Cookie headers manually since UseCookies = false
                foreach (var header in response.Headers)
                {
                    if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var cookieHeader in header.Value)
                        {
                            if (TryParseCookie(cookieHeader, out var cookieName, out var cookieValue))
                            {
                                data.COOKIES[cookieName] = cookieValue;
                            }
                        }
                    }
                }

                // Also check content headers for Set-Cookie (sometimes they're there)
                if (response.Content?.Headers != null)
                {
                    foreach (var header in response.Content.Headers)
                    {
                        if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                            header.Key.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var cookieHeader in header.Value)
                            {
                                if (TryParseCookie(cookieHeader, out var cookieName, out var cookieValue))
                                {
                                    data.COOKIES[cookieName] = cookieValue;
                                }
                            }
                        }
                    }
                }

                data.Logger.Log("Received Cookies:", LogColors.MikadoYellow);
                data.Logger.Log(data.COOKIES.Select(h => $"{h.Key}: {h.Value}"), LogColors.Khaki);
            }
            else
            {
                // Don't clear existing cookies, just skip parsing new ones
                data.Logger.Log("Cookie Parsing Skipped", LogColors.Orange);
            }

            // Decode brotli if still compressed
            if (data.HEADERS.ContainsKey("Content-Encoding") && data.HEADERS["Content-Encoding"].Contains("br"))
            {
                try
                {
                    using var inputStream = new MemoryStream(data.RAWSOURCE);
                    using var outputStream = new MemoryStream();
                    await using var brotli = new BrotliStream(inputStream, CompressionMode.Decompress, false);
                    await brotli.CopyToAsync(outputStream);
                    data.RAWSOURCE = outputStream.ToArray();
                }
                catch
                {
                    data.Logger.Log("[WARNING] Tried to decompress brotli but failed", LogColors.DarkOrange);
                }
            }

            // Unzip the GZipped content if still gzipped (after Content-Length calculation)
            if (data.RAWSOURCE.Length > 1 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B)
            {
                try
                {
                    data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
                }
                catch
                {
                    data.Logger.Log("[WARNING] Tried to decompress gzip but failed", LogColors.DarkOrange);
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
