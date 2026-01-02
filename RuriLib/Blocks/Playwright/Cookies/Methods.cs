using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Cookies
{
    [BlockCategory("Cookies", "Blocks for managing cookies in Playwright browsers", "#daa520")]
    public static class Methods
    {


        [Block("Gets a specific cookie by name", name = "Get Cookie")]
        public static async Task PlaywrightGetCookie(BotData data, string cookieName, string variableName = "cookieValue")
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var cookies = await page.Context.CookiesAsync();
            var cookie = cookies.FirstOrDefault(c => c.Name == cookieName);
            var value = cookie?.Value ?? "";
            data.SetObject(variableName, value);

            data.Logger.Log($"Got cookie '{cookieName}': {value}", LogColors.Orange);
        }

        [Block("Sets a cookie", name = "Set Cookie")]
        public static async Task PlaywrightSetCookie(BotData data, string name, string value, string domain = "", string path = "/", bool httpOnly = false, bool secure = false)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);

            if (string.IsNullOrEmpty(domain))
            {
                var url = new Uri(page.Url);
                domain = url.Host;
            }

            var cookie = new Cookie
            {
                Name = name,
                Value = value,
                Domain = domain,
                Path = path,
                HttpOnly = httpOnly,
                Secure = secure
            };

            await page.Context.AddCookiesAsync(new[] { cookie });

            data.Logger.Log($"Set cookie '{name}' = '{value}' for domain '{domain}'", LogColors.Orange);
        }

        [Block("Deletes a specific cookie", name = "Delete Cookie")]
        public static async Task PlaywrightDeleteCookie(BotData data, string cookieName)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var cookies = await page.Context.CookiesAsync();
            var cookieToDelete = cookies.FirstOrDefault(c => c.Name == cookieName);

            if (cookieToDelete != null)
            {
                // Create a cookie with the same name but expired
                var expiredCookie = new Cookie
                {
                    Name = cookieToDelete.Name,
                    Value = "",
                    Domain = cookieToDelete.Domain,
                    Path = cookieToDelete.Path,
                    Expires = DateTimeOffset.Now.AddDays(-1).ToUnixTimeSeconds()
                };

                await page.Context.AddCookiesAsync(new[] { expiredCookie });
                data.Logger.Log($"Deleted cookie: {cookieName}", LogColors.Orange);
            }
            else
            {
                data.Logger.Log($"Cookie '{cookieName}' not found", LogColors.Orange);
            }
        }

        [Block("Clears all cookies", name = "Clear All Cookies")]
        public static async Task PlaywrightClearAllCookies(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.Context.ClearCookiesAsync();

            data.Logger.Log("Cleared all cookies", LogColors.Orange);
        }

        [Block("Loads cookies from a string", name = "Load Cookies")]
        public static async Task PlaywrightLoadCookies(BotData data, [MultiLine] string cookiesString, string domain = "")
        {
            data.Logger.LogHeader();

            var page = GetPage(data);

            if (string.IsNullOrEmpty(domain))
            {
                var url = new Uri(page.Url);
                domain = url.Host;
            }

            var cookies = new List<Cookie>();
            var cookiePairs = cookiesString.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in cookiePairs)
            {
                var parts = pair.Trim().Split('=', 2);
                if (parts.Length == 2)
                {
                    cookies.Add(new Cookie
                    {
                        Name = parts[0].Trim(),
                        Value = parts[1].Trim(),
                        Domain = domain,
                        Path = "/"
                    });
                }
            }

            await page.Context.AddCookiesAsync(cookies);

            data.Logger.Log($"Loaded {cookies.Count} cookies for domain '{domain}'", LogColors.Orange);
        }

        [Block("Exports cookies to a string", name = "Export Cookies")]
        public static async Task PlaywrightExportCookies(BotData data, string variableName = "cookiesString")
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var cookies = await page.Context.CookiesAsync();
            var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            data.SetObject(variableName, cookieString);

            data.Logger.Log($"Exported {cookies.Count} cookies to string", LogColors.Orange);
        }

        [Block("Checks if a cookie exists", name = "Cookie Exists")]
        public static async Task PlaywrightCookieExists(BotData data, string cookieName, string variableName = "exists")
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var cookies = await page.Context.CookiesAsync();
            var exists = cookies.Any(c => c.Name == cookieName);
            data.SetObject(variableName, exists);

            data.Logger.Log($"Cookie '{cookieName}' exists: {exists}", LogColors.Orange);
        }



        [Block("Sets multiple cookies from a dictionary", name = "Set Multiple Cookies")]
        public static async Task PlaywrightSetMultipleCookies(BotData data, Dictionary<string, string> cookieDict, string domain = "", string path = "/")
        {
            data.Logger.LogHeader();

            var page = GetPage(data);

            if (string.IsNullOrEmpty(domain))
            {
                var url = new Uri(page.Url);
                domain = url.Host;
            }

            var cookies = cookieDict.Select(kvp => new Cookie
            {
                Name = kvp.Key,
                Value = kvp.Value,
                Domain = domain,
                Path = path
            }).ToArray();

            await page.Context.AddCookiesAsync(cookies);

            data.Logger.Log($"Set {cookies.Length} cookies for domain '{domain}'", LogColors.Orange);
        }

        [Block("Gets the cookies for a given domain from the browser. If the domain is empty, gets all cookies from the page.", name = "Get Cookies")]
        public static async Task<Dictionary<string, string>> PlaywrightGetCookies(BotData data, string domain)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var cookies = await page.Context.CookiesAsync();

            IEnumerable<BrowserContextCookiesResult> filteredCookies = cookies;

            if (!string.IsNullOrWhiteSpace(domain))
            {
                filteredCookies = cookies.Where(c => c.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase));
            }

            var resultCookies = filteredCookies.ToList();

            data.Logger.Log($"Got {resultCookies.Count} cookies for {(string.IsNullOrWhiteSpace(domain) ? "all domains" : domain)}", LogColors.Orange);
            return resultCookies.ToDictionary(c => c.Name, c => c.Value);
        }

        private static IPage GetPage(BotData data) => PlaywrightHelpers.GetPage(data);
    }
}