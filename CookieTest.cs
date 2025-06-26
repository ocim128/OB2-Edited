using System;
using System.Collections.Generic;
using System.Net;
using RuriLib.Http;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing cookie parsing fixes...");
        Console.WriteLine();
        
        // Test individual cookies as they would come from separate Set-Cookie headers
        TestCookie("csrftoken=lMKlQoWd8qRCdz1rkv5igbw6S2nh0D9A; Domain=.instagram.com; expires=Thu, 30-Jul-2026 03:00:00 GMT; Max-Age=34560000; Path=/; Secure", "csrftoken", "lMKlQoWd8qRCdz1rkv5igbw6S2nh0D9A");
        
        TestCookie("rur=\"EAG\\0544765013695\\0541782356400:01fe6d87a95c3d860c7529afb9b38e3db57b99a124252d12613d8398737ceef27fc8827a\"; Domain=.instagram.com; HttpOnly; Path=/; SameSite=Lax; Secure", "rur", "\"EAG\\0544765013695\\0541782356400:01fe6d87a95c3d860c7529afb9b38e3db57b99a124252d12613d8398737ceef27fc8827a\"");
        
        TestCookie("ds_user_id=4765013695; Domain=.instagram.com; expires=Tue, 23-Sep-2025 03:00:00 GMT; Max-Age=7776000; Path=/; SameSite=None; Secure", "ds_user_id", "4765013695");
    }
    
    static void TestCookie(string cookieHeader, string expectedName, string expectedValue)
    {
        var cookieContainer = new CookieContainer();
        var uri = new Uri("https://instagram.com");
        
        Console.WriteLine($"Testing: {cookieHeader}");
        
        HttpResponseMessageBuilder.SetCookie(cookieHeader, cookieContainer, uri);
        
        var cookies = cookieContainer.GetCookies(uri);
        if (cookies.Count == 1)
        {
            var cookie = cookies[0];
            bool nameMatch = cookie.Name == expectedName;
            bool valueMatch = cookie.Value == expectedValue;
            
            Console.WriteLine($"✓ Name: {cookie.Name} (expected: {expectedName}) - {(nameMatch ? "PASS" : "FAIL")}");
            Console.WriteLine($"✓ Value: {cookie.Value} (expected: {expectedValue}) - {(valueMatch ? "PASS" : "FAIL")}");
            
            if (nameMatch && valueMatch)
            {
                Console.WriteLine("✅ TEST PASSED");
            }
            else
            {
                Console.WriteLine("❌ TEST FAILED");
            }
        }
        else
        {
            Console.WriteLine($"❌ Expected 1 cookie, got {cookies.Count}");
        }
        
        Console.WriteLine();
    }

    static string[] SplitCookies(string cookieHeader)
    {
        // This is a simplified version of cookie splitting
        // In practice, each Set-Cookie header should be processed separately
        // But for testing purposes, we'll split manually based on the known pattern
        
        // Look for patterns like ", cookieName=" 
        var cookies = new List<string>();
        var current = "";
        var inQuotes = false;
        
        for (int i = 0; i < cookieHeader.Length; i++)
        {
            char c = cookieHeader[i];
            current += c;
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                // Check if this looks like a cookie boundary
                if (i + 1 < cookieHeader.Length && cookieHeader[i + 1] == ' ')
                {
                    // Look ahead to see if next token looks like a cookie name (has = after it)
                    int nextSpace = cookieHeader.IndexOf(' ', i + 2);
                    int nextEquals = cookieHeader.IndexOf('=', i + 2);
                    
                    if (nextEquals != -1 && (nextSpace == -1 || nextEquals < nextSpace))
                    {
                        // This looks like a cookie boundary
                        cookies.Add(current.Substring(0, current.Length - 1)); // Remove the comma
                        current = "";
                        i++; // Skip the space
                    }
                }
            }
        }
        
        if (!string.IsNullOrEmpty(current))
        {
            cookies.Add(current);
        }
        
        return cookies.ToArray();
    }
}
