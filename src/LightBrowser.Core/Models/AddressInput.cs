namespace SudyBrowser.Core.Models;

public enum AddressInputKind
{
    /// <summary>The input is (or was completed to) a navigable URL.</summary>
    Url,

    /// <summary>The input is a free-text query that was turned into a search URL.</summary>
    Search,
}

/// <summary>Result of interpreting text typed into the address bar.</summary>
public sealed record AddressInput(Uri Target, AddressInputKind Kind);
