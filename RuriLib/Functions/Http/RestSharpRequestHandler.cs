using RestSharp;
using RuriLib.Extensions;
using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    internal class RestSharpRequestHandler : HttpRequestHandler
    {
        public async override Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            var restOptions = new RestClientOptions(options.Url)
            {
                FollowRedirects = options.AutoRedirect,
                MaxRedirects = options.MaxNumberOfRedirects,
                Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
                CookieContainer = new CookieContainer()
            };

            if (data.UseProxy)
            {
                var proxy = data.Proxy;
                var proxyUri = new Uri($"{proxy.Host}:{proxy.Port}");
                restOptions.Proxy = new WebProxy(proxyUri);
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    restOptions.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }
            }

            using var client = new RestClient(restOptions);
            var request = new RestRequest(options.Url, (Method)Enum.Parse(typeof(Method), options.Method.ToString()));

            foreach (var header in options.CustomHeaders)
                request.AddHeader(header.Key, header.Value);

            foreach (var cookie in data.COOKIES)
                request.AddCookie(cookie.Key, cookie.Value, "/", new Uri(options.Url).Host);

            if (!string.IsNullOrEmpty(options.Content))
                request.AddParameter(options.ContentType, options.Content, ParameterType.RequestBody);

            var response = await client.ExecuteAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public async override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            var restOptions = new RestClientOptions(options.Url)
            {
                FollowRedirects = options.AutoRedirect,
                MaxRedirects = options.MaxNumberOfRedirects,
                Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
                CookieContainer = new CookieContainer()
            };

            if (data.UseProxy)
            {
                var proxy = data.Proxy;
                var proxyUri = new Uri($"{proxy.Host}:{proxy.Port}");
                restOptions.Proxy = new WebProxy(proxyUri);
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    restOptions.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }
            }

            using var client = new RestClient(restOptions);
            var request = new RestRequest(options.Url, (Method)Enum.Parse(typeof(Method), options.Method.ToString()));

            foreach (var header in options.CustomHeaders)
                request.AddHeader(header.Key, header.Value);

            foreach (var cookie in data.COOKIES)
                request.AddCookie(cookie.Key, cookie.Value, "/", new Uri(options.Url).Host);

            request.AddParameter(options.ContentType, options.Content, ParameterType.RequestBody);

            var response = await client.ExecuteAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public async override Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            var restOptions = new RestClientOptions(options.Url)
            {
                FollowRedirects = options.AutoRedirect,
                MaxRedirects = options.MaxNumberOfRedirects,
                Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
                CookieContainer = new CookieContainer()
            };

            if (data.UseProxy)
            {
                var proxy = data.Proxy;
                var proxyUri = new Uri($"{proxy.Host}:{proxy.Port}");
                restOptions.Proxy = new WebProxy(proxyUri);
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    restOptions.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }
            }

            using var client = new RestClient(restOptions);
            client.AddDefaultHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));
            
            var request = new RestRequest(options.Url, (Method)Enum.Parse(typeof(Method), options.Method.ToString()));

            foreach (var header in options.CustomHeaders)
                request.AddHeader(header.Key, header.Value);

            foreach (var cookie in data.COOKIES)
                request.AddCookie(cookie.Key, cookie.Value, "/", new Uri(options.Url).Host);

            var response = await client.ExecuteAsync(request, data.CancellationToken).ConfigureAwait(false);
            await LogResponse(data, response, options).ConfigureAwait(false);
        }

        public override Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options) => throw new NotImplementedException();

        private async Task LogResponse(BotData data, RestResponse response, RuriLib.Functions.Http.Options.HttpRequestOptions options)
        {
            data.RESPONSECODE = (int)response.StatusCode;
            data.ADDRESS = response.ResponseUri?.AbsoluteUri ?? options.Url;
            data.RAWSOURCE = response.RawBytes ?? Array.Empty<byte>();

            data.HEADERS.Clear();
            if (response.Headers != null)
            {
                foreach (var header in response.Headers)
                {
                    data.HEADERS[header.Name] = header.Value?.ToString();
                }
            }

            if (response.Cookies != null)
            {
                foreach (System.Net.Cookie cookie in response.Cookies)
                {
                    data.COOKIES[cookie.Name] = cookie.Value;
                }
            }

            data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);
            data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);
            
            if (data.RAWSOURCE.Length > 0)
            {
                data.Logger.Log($"Received {data.RAWSOURCE.Length} bytes", LogColors.White);
            }
        }
    }
}
