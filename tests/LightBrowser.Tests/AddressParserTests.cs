using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;

namespace SudyBrowser.Tests;

public class AddressParserTests
{
    private readonly AddressParser _parser = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_ReturnsNull(string? text) => Assert.Null(_parser.Parse(text));

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("  example.com  ", "https://example.com/")]
    [InlineData("www.example.com/path?q=1", "https://www.example.com/path?q=1")]
    [InlineData("sub.example.co.uk:8443", "https://sub.example.co.uk:8443/")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("HTTPS://Example.com/A", "https://example.com/A")]
    [InlineData("about:blank", "about:blank")]
    public void DomainOrUrl_IsNavigatedTo(string text, string expected)
    {
        var result = _parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(AddressInputKind.Url, result.Kind);
        Assert.Equal(expected, result.Target.AbsoluteUri);
    }

    [Theory]
    [InlineData("localhost", "http://localhost/")]
    [InlineData("localhost:3000/app", "http://localhost:3000/app")]
    [InlineData("127.0.0.1:8080", "http://127.0.0.1:8080/")]
    [InlineData("[::1]:5000", "http://[::1]:5000/")]
    public void LocalAddresses_DefaultToHttp(string text, string expected)
    {
        var result = _parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(AddressInputKind.Url, result.Kind);
        Assert.Equal(expected, result.Target.AbsoluteUri);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("weather")]
    [InlineData("what is c++?")]
    [InlineData("example .com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.com")]
    public void QueriesAndUnsupportedSchemes_BecomeSearch(string text)
    {
        var result = _parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(AddressInputKind.Search, result.Kind);
        Assert.StartsWith("https://www.google.com/search?q=", result.Target.AbsoluteUri);
    }

    [Fact]
    public void Search_EscapesQuery()
    {
        var result = _parser.Parse("a&b c");

        Assert.NotNull(result);
        Assert.Equal("https://www.google.com/search?q=a%26b%20c", result.Target.AbsoluteUri);
    }
}
