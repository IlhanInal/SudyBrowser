using System.Windows.Media;

namespace SudyBrowser.App.Models;

/// <summary>A UI theme: a resource dictionary that defines the shared brush keys.</summary>
public sealed record ThemeOption(string Name, string Key, Uri Source, Brush Swatch);
