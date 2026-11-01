using LightBrowser.Core.Models;

namespace LightBrowser.App.Models;

/// <summary>One of the fixed shortcut tiles on the home page; <see cref="Site"/> is null for an empty slot.</summary>
public sealed record ShortcutSlot(RecentSite? Site)
{
    public bool HasSite => Site is not null;
}
