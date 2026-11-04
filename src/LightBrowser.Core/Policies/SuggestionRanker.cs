namespace SudyBrowser.Core.Policies;

/// <summary>Scores how well a stored page matches what the user typed. Pure, so it is cheap to test.</summary>
public static class SuggestionRanker
{
    public const int NoMatch = -1;

    private const int TierStep = 1000;
    private const int HostPrefixTier = 6;
    private const int AddressPrefixTier = 5;
    private const int TitlePrefixTier = 4;
    private const int WordPrefixTier = 3;
    private const int HostContainsTier = 2;
    private const int OtherContainsTier = 1;

    private const int RootPageBonus = 200;
    private const int MaxVisitBonusCount = 50;
    private const int VisitBonusWeight = 2;
    private const int DayBonus = 30;
    private const int WeekBonus = 20;
    private const int MonthBonus = 10;

    /// <summary>Returns <see cref="NoMatch"/>, or a score where a higher value ranks first.</summary>
    /// <param name="normalizedQuery">Output of <see cref="UrlText.NormalizeQuery"/>; must not be empty.</param>
    public static int Score(
        string normalizedQuery,
        string url,
        string title,
        int visitCount,
        DateTime lastVisitUtc,
        DateTime nowUtc)
    {
        var address = UrlText.StripSchemeAndWww(url);
        var host = UrlText.HostOf(address);

        int tier;
        if (host.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            tier = HostPrefixTier;
        }
        else if (address.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            tier = AddressPrefixTier;
        }
        else if (title.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            tier = TitlePrefixTier;
        }
        else if (HasWordStartingWith(title, normalizedQuery))
        {
            tier = WordPrefixTier;
        }
        else if (host.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            tier = HostContainsTier;
        }
        else if (address.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                 || title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            tier = OtherContainsTier;
        }
        else
        {
            return NoMatch;
        }

        var score = tier * TierStep;
        if (tier == HostPrefixTier && address.Length == host.Length + (address.EndsWith('/') ? 1 : 0))
        {
            // The site's front page beats a deep link on the same host.
            score += RootPageBonus;
        }

        score += Math.Min(visitCount, MaxVisitBonusCount) * VisitBonusWeight;
        var age = nowUtc - lastVisitUtc;
        score += age < TimeSpan.FromDays(1) ? DayBonus
            : age < TimeSpan.FromDays(7) ? WeekBonus
            : age < TimeSpan.FromDays(30) ? MonthBonus
            : 0;
        return score;
    }

    private static bool HasWordStartingWith(string text, string prefix)
    {
        var index = 0;
        while ((index = text.IndexOf(prefix, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                return true;
            }

            index++;
        }

        return false;
    }
}
