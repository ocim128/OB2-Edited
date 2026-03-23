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

        public NormalizedHttpRequest CreateRedirect(Uri targetUri, Dictionary<string, string> headers)
            => new()
            {
                Uri = targetUri,
                Method = System.Net.Http.HttpMethod.Get,
                Version = Version,
                Headers = headers,
                Cookies = Cookies,
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

    internal sealed class NormalizedHttpResponse
    {
        public required Uri Address { get; init; }
        public required int StatusCode { get; init; }
        public required Dictionary<string, List<string>> Headers { get; init; }
        public byte[] RawBody { get; init; } = Array.Empty<byte>();
    }

    internal readonly record struct ParsedCookie(string Name, string Value, string RawValue);
}
