using System;
using System.Net;
using Xunit;

namespace RuriLib.Http.Tests;

public class CookieTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("undefined")]
    [InlineData("cookie1")]
    [InlineData(";")]
    public void SetCookie_EmptyString_DoNothing(string cookie)
    {
        var cookies = new CookieContainer();
        var uri = new Uri("http://example.com");
        
        HttpResponseMessageBuilder.SetCookie(cookie, cookies, uri);
        
        var cookieCollection = cookies.GetCookies(uri);
        Assert.Empty(cookieCollection);
    }
    
    [Theory]
    [InlineData("cookie1=value1")]
    [InlineData("cookie1=value1;")]
    [InlineData("cookie1=value1; ")]
    [InlineData("cookie1=value1; Domain=example.com; Path=/; Secure; HttpOnly")]
    public void SetCookie_SingleCookie_SetSuccessfully(string cookie)
    {
        var cookies = new CookieContainer();
        var uri = new Uri("http://example.com");
        
        HttpResponseMessageBuilder.SetCookie(cookie, cookies, uri);
        
        var cookieCollection = cookies.GetCookies(uri);
        Assert.Single(cookieCollection);
        Assert.Equal("cookie1", cookieCollection[0].Name);
        Assert.Equal("value1", cookieCollection[0].Value);
    }
    
    [Theory]
    [InlineData("cookie1=value1")]
    [InlineData("cookie1=value1; Domain=example.com; Path=/; Secure; HttpOnly")]
    [InlineData("rur=\"EAG\\0544765013695\\0541782356400:01fe6d87a95c3d860c7529afb9b38e3db57b99a124252d12613d8398737ceef27fc8827a\"")]
    [InlineData("csrftoken=lMKlQoWd8qRCdz1rkv5igbw6S2nh0D9A; Domain=.instagram.com; expires=Thu, 30-Jul-2026 03:00:00 GMT; Max-Age=34560000; Path=/; Secure")]
    public void SetCookie_CookieWithAttributes_ExtractNameAndValueOnly(string cookieHeader)
    {
        var cookiesContainer = new CookieContainer();
        var uri = new Uri("http://example.com");
        
        HttpResponseMessageBuilder.SetCookies(cookieHeader, cookiesContainer, uri);
        
        var cookieCollection = cookiesContainer.GetCookies(uri);
        Assert.Single(cookieCollection);
        
        // Test that we correctly extract just the name and value, ignoring attributes
        if (cookieHeader.StartsWith("cookie1"))
        {
            Assert.Equal("cookie1", cookieCollection[0].Name);
            Assert.Equal("value1", cookieCollection[0].Value);
        }
        else if (cookieHeader.StartsWith("rur"))
        {
            Assert.Equal("rur", cookieCollection[0].Name);
            Assert.Equal("\"EAG\\0544765013695\\0541782356400:01fe6d87a95c3d860c7529afb9b38e3db57b99a124252d12613d8398737ceef27fc8827a\"", cookieCollection[0].Value);
        }
        else if (cookieHeader.StartsWith("csrftoken"))
        {
            Assert.Equal("csrftoken", cookieCollection[0].Name);
            Assert.Equal("lMKlQoWd8qRCdz1rkv5igbw6S2nh0D9A", cookieCollection[0].Value);
        }
    }
}
