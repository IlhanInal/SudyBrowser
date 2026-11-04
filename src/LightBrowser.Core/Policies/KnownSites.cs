namespace SudyBrowser.Core.Policies;

/// <summary>A configured, always-available site suggestion.</summary>
public sealed record KnownSite(string Url, string Title);

public static class KnownSites
{
    /// <summary>Starter list so completion works on a fresh profile with an empty history.</summary>
    public static IReadOnlyList<KnownSite> Default { get; } = new KnownSite[]
    {
        new("https://www.google.com/", "Google"),
        new("https://www.youtube.com/", "YouTube"),
        new("https://github.com/", "GitHub"),
        new("https://www.sahibinden.com/", "sahibinden.com"),
        new("https://www.wikipedia.org/", "Wikipedia"),
        new("https://www.reddit.com/", "Reddit"),
        new("https://x.com/", "X"),
        new("https://stackoverflow.com/", "Stack Overflow"),
        new("https://www.netflix.com/", "Netflix"),
        new("https://open.spotify.com/", "Spotify"),
        new("https://www.linkedin.com/", "LinkedIn"),
        new("https://www.amazon.com/", "Amazon"),
        new("https://www.twitch.tv/", "Twitch"),
    };
}
