using RuriLib.Helpers.CSharp;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuriLib.Models.Blocks.Custom;

public partial class HttpRequestBlockInstance
{
    public override string ToCSharp(List<string> definedVariables, ConfigSettings settings)
    {
        NormalizeLegacySettings();

        using var writer = new StringWriter();

        if (Safe)
        {
            writer.WriteLine("try {");
        }

        writer.Write("await ");

        switch (RequestParams)
        {
            case StandardRequestParams x:
                writer.Write("HttpRequestStandard(data, new StandardHttpRequestOptions { ");
                writer.Write("Content = " + CSharpWriter.FromSetting(x.Content) + ", ");
                writer.Write("ContentType = " + CSharpWriter.FromSetting(x.ContentType) + ", ");
                writer.Write("UrlEncodeContent = " + GetSettingValue("urlEncodeContent") + ", ");
                break;

            case RawRequestParams x:
                writer.Write("HttpRequestRaw(data, new RawHttpRequestOptions { ");
                writer.Write("Content = " + CSharpWriter.FromSetting(x.Content) + ", ");
                writer.Write("ContentType = " + CSharpWriter.FromSetting(x.ContentType) + ", ");
                break;

            case BasicAuthRequestParams x:
                writer.Write("HttpRequestBasicAuth(data, new BasicAuthHttpRequestOptions { ");
                writer.Write("Username = " + CSharpWriter.FromSetting(x.Username) + ", ");
                writer.Write("Password = " + CSharpWriter.FromSetting(x.Password) + ", ");
                break;

            case MultipartRequestParams x:
                writer.Write("HttpRequestMultipart(data, new MultipartHttpRequestOptions { ");
                writer.Write("Boundary = " + CSharpWriter.FromSetting(x.Boundary) + ", ");
                writer.Write("Contents = " + SerializeMultipart(x.Contents) + ", ");
                break;
        }

        writer.Write("Url = " + GetSettingValue("url") + ", ");
        writer.Write("Method = " + GetSettingValue("method") + ", ");
        writer.Write("AutoRedirect = " + GetSettingValue("autoRedirect") + ", ");
        writer.Write("MaxNumberOfRedirects = " + GetSettingValue("maxNumberOfRedirects") + ", ");
        writer.Write("ReadResponseContent = " + GetSettingValue("readResponseContent") + ", ");
        writer.Write("AbsoluteUriInFirstLine = " + GetSettingValue("absoluteUriInFirstLine") + ", ");
        writer.Write("HttpLibrary = " + GetSettingValue("httpLibrary") + ", ");
        writer.Write("UseTlsFingerprinting = " + GetSettingValue("useTlsFingerprinting") + ", ");
        writer.Write("TlsClientProfile = " + GetSettingValue("tlsClientProfile") + ", ");
        writer.Write("CustomJa3String = " + GetSettingValue("customJa3String") + ", ");
        writer.Write("RandomizeTlsExtensionOrder = " + GetSettingValue("randomizeTlsExtensionOrder") + ", ");
        writer.Write("InsecureSkipVerify = " + GetSettingValue("insecureSkipVerify") + ", ");
        writer.Write("SecurityProtocol = " + GetSettingValue("securityProtocol") + ", ");
        writer.Write("CustomCookies = " + GetSettingValue("customCookies") + ", ");
        writer.Write("CustomHeaders = " + GetSettingValue("customHeaders") + ", ");
        writer.Write("TimeoutMilliseconds = " + GetSettingValue("timeoutMilliseconds") + ", ");
        writer.Write("HttpVersion = " + GetSettingValue("httpVersion") + ", ");
        writer.Write("CodePagesEncoding = " + GetSettingValue("codePagesEncoding") + ", ");
        writer.Write("AlwaysSendContent = " + GetSettingValue("alwaysSendContent") + ", ");
        writer.Write("DecodeHtml = " + GetSettingValue("decodeHtml") + ", ");
        writer.Write("DisableCookieParsing = " + GetSettingValue("disableCookieParsing") + ", ");
        writer.Write("DisableHeaderParsing = " + GetSettingValue("disableHeaderParsing") + ", ");
        writer.Write("UseCustomCipherSuites = " + GetSettingValue("useCustomCipherSuites") + ", ");
        writer.Write("CustomCipherSuites = " + GetSettingValue("customCipherSuites") + " ");

        writer.WriteLine("}).ConfigureAwait(false);");

        if (Safe)
        {
            writer.WriteLine("} catch (Exception safeException) {");
            writer.WriteLine("data.ERROR = safeException.PrettyPrint();");
            writer.WriteLine("data.Logger.Log($\"[SAFE MODE] Exception caught and saved to data.ERROR: {data.ERROR}\", LogColors.Tomato); }");
        }

        return writer.ToString();
    }

    private static string SerializeMultipart(List<HttpContentSettingsGroup> contents)
        => $"new List<MyHttpContent> {{ {string.Join(", ", contents.Select(SerializeContent))} }}";

    private static string SerializeContent(HttpContentSettingsGroup content) => content switch
    {
        StringHttpContentSettingsGroup x =>
            $"new StringHttpContent({CSharpWriter.FromSetting(x.Name)}, {CSharpWriter.FromSetting(x.Data)}, {CSharpWriter.FromSetting(x.ContentType)})",
        RawHttpContentSettingsGroup x =>
            $"new RawHttpContent({CSharpWriter.FromSetting(x.Name)}, {CSharpWriter.FromSetting(x.Data)}, {CSharpWriter.FromSetting(x.ContentType)})",
        FileHttpContentSettingsGroup x =>
            $"new FileHttpContent({CSharpWriter.FromSetting(x.Name)}, {CSharpWriter.FromSetting(x.FileName)}, {CSharpWriter.FromSetting(x.ContentType)})",
        _ => throw new NotImplementedException()
    };

    private string GetSettingValue(string name)
        => CSharpWriter.FromSetting(Settings[name]);
}
