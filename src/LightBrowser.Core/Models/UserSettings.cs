namespace SudyBrowser.Core.Models;

/// <summary>Persisted user preferences (theme, home background, online suggestions, etc.).</summary>
public sealed record UserSettings(
    string ThemeFile = "Light",
    string BackgroundFile = "mountain",
    bool OnlineSuggestionsEnabled = false);
