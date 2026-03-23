using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using System;
using System.Collections.Generic;

namespace RuriLib.Helpers.Blocks;

internal static class BuiltInBlockRegistry
{
    public static IReadOnlyList<Func<BlockDescriptor>> CustomDescriptorFactories { get; } =
    [
        static () => new KeycheckBlockDescriptor(),
        static () => new HttpRequestBlockDescriptor(),
        static () => new ParseBlockDescriptor(),
        static () => new ScriptBlockDescriptor()
    ];

    public static IReadOnlyList<Type> ExposedMethodTypes { get; } =
    [
        typeof(global::RuriLib.Blocks.Android.Actions.Methods),
        typeof(global::RuriLib.Blocks.Android.Driver.Methods),
        typeof(global::RuriLib.Blocks.Android.Elements.Methods),
        typeof(global::RuriLib.Blocks.Captchas.Methods),
        typeof(global::RuriLib.Blocks.Functions.Methods),
        typeof(global::RuriLib.Blocks.Functions.ByteArray.Methods),
        typeof(global::RuriLib.Blocks.Functions.Constants.CreateMultipleConstant),
        typeof(global::RuriLib.Blocks.Functions.Constants.Methods),
        typeof(global::RuriLib.Blocks.Functions.Crypto.Methods),
        typeof(global::RuriLib.Blocks.Functions.Dictionary.Methods),
        typeof(global::RuriLib.Blocks.Functions.Float.Methods),
        typeof(global::RuriLib.Blocks.Functions.Integer.Methods),
        typeof(global::RuriLib.Blocks.Functions.List.Methods),
        typeof(global::RuriLib.Blocks.Functions.String.Methods),
        typeof(global::RuriLib.Blocks.Functions.Time.Methods),
        typeof(global::RuriLib.Blocks.Interop.Methods),
        typeof(global::RuriLib.Blocks.Playwright.Browser.Methods),
        typeof(global::RuriLib.Blocks.Playwright.Cookies.Methods),
        typeof(global::RuriLib.Blocks.Playwright.Elements.Methods),
        typeof(global::RuriLib.Blocks.Playwright.Page.Methods),
        typeof(global::RuriLib.Blocks.Puppeteer.Browser.Methods),
        typeof(global::RuriLib.Blocks.Puppeteer.Elements.Methods),
        typeof(global::RuriLib.Blocks.Puppeteer.Page.Methods),
        typeof(global::RuriLib.Blocks.Requests.Ftp.Methods),
        typeof(global::RuriLib.Blocks.Requests.Imap.Methods),
        typeof(global::RuriLib.Blocks.Requests.Pop3.Methods),
        typeof(global::RuriLib.Blocks.Requests.Smtp.Methods),
        typeof(global::RuriLib.Blocks.Requests.Ssh.Methods),
        typeof(global::RuriLib.Blocks.Requests.Tcp.Methods),
        typeof(global::RuriLib.Blocks.Requests.WebSocket.Methods),
        typeof(global::RuriLib.Blocks.Selenium.Browser.Methods),
        typeof(global::RuriLib.Blocks.Selenium.Elements.Methods),
        typeof(global::RuriLib.Blocks.Selenium.Page.Methods),
        typeof(global::RuriLib.Blocks.Utility.Audio.Methods),
        typeof(global::RuriLib.Blocks.Utility.Conversion.Methods),
        typeof(global::RuriLib.Blocks.Utility.Files.Methods),
        typeof(global::RuriLib.Blocks.Utility.Images.Methods),
        typeof(global::RuriLib.Blocks.Utility.Methods),
        typeof(global::RuriLib.Blocks.Utility.PairTrading.Methods)
    ];

    public static void RegisterBuiltIns(DescriptorsRepository repository)
    {
        foreach (var descriptorFactory in CustomDescriptorFactories)
        {
            repository.AddDescriptor(descriptorFactory());
        }

        repository.AddFromExposedMethods(ExposedMethodTypes);
    }
}
