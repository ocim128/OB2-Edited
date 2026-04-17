using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using System;
using System.Collections.Generic;
namespace RuriLib.Functions.Http
{
    internal sealed class NormalizedHttpRequest
    {
        public required Uri Uri { get; init; }
        public required System.Net.Http.HttpMethod Method { get; init; }
        public required Version Version { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        public required IDictionary<string, string> Cookies { get; init; }
        public string? StringBody { get; set; }
        public byte[]? RawBody { get; set; }
        public List<MyHttpContent>? MultipartContents { get; set; }
        public string? Boundary { get; set; }
        public string? LoggedContent { get; set; }
        public string? ContentType { get; set; }
        public string? ContentLengthDisplay { get; set; }
        public string? RedirectAuthorization { get; set; }
        public bool AutoRedirect { get; init; }
        public int RemainingRedirects { get; init; }
        public int TimeoutMilliseconds { get; init; }
        public bool AbsoluteUriInFirstLine { get; init; }
        public bool ReadResponseContent { get; init; }
        public bool DecodeHtml { get; init; }
        public bool DisableCookieParsing { get; init; }
        public bool DisableHeaderParsing { get; init; }
        public string CodePagesEncoding { get; init; } = string.Empty;
        public bool AllowHttpsToHttpRedirect { get; init; }

        /// <summary>
        /// Creates a redirect request using the runtime redirect behavior:
        /// 307/308 preserve method and body, 301/302 preserve non-POST methods,
        /// and 303 rewrites to GET except for HEAD.
        /// </summary>
        public NormalizedHttpRequest CreateRedirect(Uri targetUri, Dictionary<string, string> headers, int statusCode)
        {
            var redirectMethod = GetRedirectMethod(Method, statusCode);
            var preserveBody = ShouldPreserveBody(Method, redirectMethod, statusCode);
            return new()
            {
                Uri = targetUri,
                Method = redirectMethod,
                Version = Version,
                Headers = headers,
                Cookies = Cookies,
                StringBody = preserveBody ? StringBody : null,
                RawBody = preserveBody ? RawBody : null,
                MultipartContents = preserveBody ? MultipartContents : null,
                Boundary = preserveBody ? Boundary : null,
                ContentType = preserveBody ? ContentType : null,
                ContentLengthDisplay = preserveBody ? ContentLengthDisplay : null,
                LoggedContent = null,
                RedirectAuthorization = RedirectAuthorization,
                AutoRedirect = AutoRedirect,
                RemainingRedirects = RemainingRedirects - 1,
                TimeoutMilliseconds = TimeoutMilliseconds,
                AbsoluteUriInFirstLine = AbsoluteUriInFirstLine,
                ReadResponseContent = ReadResponseContent,
                DecodeHtml = DecodeHtml,
                DisableCookieParsing = DisableCookieParsing,
                DisableHeaderParsing = DisableHeaderParsing,
                CodePagesEncoding = CodePagesEncoding,
                AllowHttpsToHttpRedirect = AllowHttpsToHttpRedirect
            };
        }

        private static System.Net.Http.HttpMethod GetRedirectMethod(System.Net.Http.HttpMethod originalMethod, int statusCode)
            => statusCode switch
            {
                307 or 308 => originalMethod,
                303 => originalMethod == System.Net.Http.HttpMethod.Head
                    ? System.Net.Http.HttpMethod.Head
                    : System.Net.Http.HttpMethod.Get,
                301 or 302 => originalMethod == System.Net.Http.HttpMethod.Post
                    ? System.Net.Http.HttpMethod.Get
                    : originalMethod,
                _ => System.Net.Http.HttpMethod.Get
            };

        private static bool ShouldPreserveBody(
            System.Net.Http.HttpMethod originalMethod,
            System.Net.Http.HttpMethod redirectMethod,
            int statusCode)
            => statusCode is 307 or 308 ||
               (statusCode is 301 or 302 &&
                redirectMethod == originalMethod &&
                originalMethod != System.Net.Http.HttpMethod.Get &&
                originalMethod != System.Net.Http.HttpMethod.Head);
    }

    internal sealed class NormalizedHttpResponse
    {
        public required Uri Address { get; init; }
        public required int StatusCode { get; init; }
        public required Dictionary<string, List<string>> Headers { get; init; }
        public byte[] RawBody { get; init; } = Array.Empty<byte>();
    }

    internal readonly record struct ParsedCookie(string Name, string Value, string RawValue);
}
