using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Policies;

/// <summary>
/// Decides whether address bar text is a URL or a search query.
/// Only http(s) and about:blank are navigable; any other scheme is treated as a search to avoid
/// surprising handlers such as javascript: or file:.
/// </summary>
public sealed class AddressParser : IAddressParser
{
    public const string SearchUrlTemplate = "https://www.google.com/search?q={0}";

    private const string SchemeSeparator = "://";
    private const string BlankPage = "about:blank";
    private const string LocalHost = "localhost";
    private const int MinTldLength = 2;

    public AddressInput? Parse(string? text)
    {
        var input = text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        if (input.Equals(BlankPage, StringComparison.OrdinalIgnoreCase))
        {
            return new AddressInput(new Uri(BlankPage), AddressInputKind.Url);
        }

        if (input.Any(char.IsWhiteSpace))
        {
            return Search(input);
        }

        if (input.Contains(SchemeSeparator, StringComparison.Ordinal))
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var absolute)
                && IsWebScheme(absolute)
                && !string.IsNullOrEmpty(absolute.Host)
                    ? new AddressInput(absolute, AddressInputKind.Url)
                    : Search(input);
        }

        if (Uri.TryCreate(Uri.UriSchemeHttps + SchemeSeparator + input, UriKind.Absolute, out var candidate))
        {
            if (IsLocalOrIp(candidate))
            {
                // Local servers and raw IPs rarely have TLS, so default to http.
                var local = new Uri(Uri.UriSchemeHttp + SchemeSeparator + input);
                return new AddressInput(local, AddressInputKind.Url);
            }

            if (IsDomainName(candidate))
            {
                return new AddressInput(candidate, AddressInputKind.Url);
            }
        }

        return Search(input);
    }

    private static AddressInput Search(string query)
    {
        var url = string.Format(SearchUrlTemplate, Uri.EscapeDataString(query));
        return new AddressInput(new Uri(url), AddressInputKind.Search);
    }

    private static bool IsWebScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsLocalOrIp(Uri uri) =>
        uri.Host.Equals(LocalHost, StringComparison.OrdinalIgnoreCase)
        || uri.HostNameType == UriHostNameType.IPv6
        || (uri.HostNameType == UriHostNameType.IPv4 && uri.Host.Count(c => c == '.') == 3);

    private static bool IsDomainName(Uri uri)
    {
        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = uri.IdnHost.Split('.');
        var tld = labels[^1];
        return labels.Length >= 2
            && tld.Length >= MinTldLength
            && labels.All(label => label.Length > 0)
            && (tld.All(char.IsLetter) || tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase));
    }
}
