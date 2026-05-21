using RuriLib.Blocks.Requests.Http;
using RuriLib.Functions.Http;
using RuriLib.Functions.Http.Options;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs;
using RuriLib.Models.Data;
using RuriLib.Providers.Proxies;
using RuriLib.Providers.Security;
using RuriLib.Services;
using RuriLib.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Tests.Functions.Http;

using RequestMethod = RuriLib.Functions.Http.HttpMethod;

internal sealed class HttpTransportTestContext : IDisposable
{
    public TestWorkspace Workspace { get; } = new();
    public RuriLibSettingsService Settings { get; }
    public BotData Data { get; }

    public HttpTransportTestContext()
    {
        TestAssemblyResolver.EnsureRegistered();
        Settings = new RuriLibSettingsService(Workspace.RootPath);
        Settings.RuriLibSettings.GeneralSettings.VerboseMode = true;

        var providers = new RuriLib.Models.Bots.Providers(null)
        {
            ProxySettings = new DefaultProxySettingsProvider(Settings),
            Security = new DefaultSecurityProvider(Settings)
        };
        var defaultWordlistType = Settings.Environment.WordlistTypes.First(w => w.Name == "Default");
        Data = new BotData(
            providers,
            new ConfigSettings(),
            new BotLogger(),
            new DataLine("test-data", defaultWordlistType));
    }

    public void Dispose()
    {
        if (Data.TlsClientSessionId.HasValue)
        {
            var destroySession = typeof(Methods).Assembly
                .GetType("RuriLib.Functions.Http.TlsClientRequestHandler", throwOnError: true)!
                .GetMethod("DestroySession", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            _ = destroySession?.Invoke(null, new object[] { Data.TlsClientSessionId.Value });
        }

        Workspace.Dispose();
    }
}

internal static class HttpTransportTestHelper
{
    public static IEnumerable<object[]> AllLibraries
    {
        get
        {
            yield return new object[] { HttpLibrary.SystemNet };
            yield return new object[] { HttpLibrary.RuriLibHttp };

            if (IsTlsClientAvailable())
            {
                yield return new object[] { HttpLibrary.TlsClient };
            }
        }
    }

    public static Task SendStandardAsync(
        BotData data,
        HttpLibrary library,
        Uri uri,
        RequestMethod method = RequestMethod.GET,
        string content = "",
        bool autoRedirect = true,
        int maxRedirects = 8,
        Dictionary<string, string>? customHeaders = null,
        Dictionary<string, string>? customCookies = null,
        string codePagesEncoding = "",
        bool readResponseContent = true,
        bool alwaysSendContent = false,
        bool disableCookieParsing = false)
        => Methods.HttpRequestStandard(data, new StandardHttpRequestOptions
        {
            Url = uri.ToString(),
            Method = method,
            HttpLibrary = library,
            AutoRedirect = autoRedirect,
            MaxNumberOfRedirects = maxRedirects,
            ReadResponseContent = readResponseContent,
            TimeoutMilliseconds = 15000,
            HttpVersion = "1.1",
            Content = content,
            ContentType = "text/plain; charset=utf-8",
            AlwaysSendContent = alwaysSendContent,
            CodePagesEncoding = codePagesEncoding,
            DisableCookieParsing = disableCookieParsing,
            CustomCookies = customCookies ?? new Dictionary<string, string>(),
            CustomHeaders = customHeaders ?? new Dictionary<string, string>()
        });

    public static Task SendMultipartAsync(
        BotData data,
        HttpLibrary library,
        Uri uri,
        IReadOnlyList<MyHttpContent> contents,
        string boundary = "----FluxBoundaryTest")
        => Methods.HttpRequestMultipart(data, new MultipartHttpRequestOptions
        {
            Url = uri.ToString(),
            Method = RequestMethod.POST,
            HttpLibrary = library,
            AutoRedirect = false,
            MaxNumberOfRedirects = 0,
            ReadResponseContent = true,
            TimeoutMilliseconds = 15000,
            HttpVersion = "1.1",
            Boundary = boundary,
            Contents = contents.ToList(),
            CustomCookies = new Dictionary<string, string>(),
            CustomHeaders = new Dictionary<string, string>()
        });

    public static string GetMergedHeaderValue(Dictionary<string, string> headers, string name)
    {
        var header = headers.Single(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        return header.Value;
    }

    public static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                {
                    continue;
                }

                match = false;
                break;
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    public static byte[] GetWindows1252Bytes(string value)
        => CodePagesEncodingProvider.Instance.GetEncoding("windows-1252")!.GetBytes(value);

    public static byte[] GetAsciiBytes(string value) => Encoding.ASCII.GetBytes(value);

    public static bool ByteArraysEqual(byte[] expected, byte[] actual)
        => expected.AsSpan().SequenceEqual(actual);

    private static bool IsTlsClientAvailable()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "x86";

        var candidatePaths = new[]
        {
            Path.Combine(baseDir, "tls-client.dll"),
            Path.Combine(baseDir, "runtimes", "tls-client", "win", arch, "tls-client.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "tls-client.dll"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "tlsclient.native.win-x64", "1.9.1",
                "runtimes", "tls-client", "win", "x64", "tls-client.dll")
        };

        return candidatePaths.Any(File.Exists);
    }
}
