using System.Windows.Media;

namespace LightBrowser.App.Models;

/// <summary>A UI theme: a resource dictionary that defines the shared brush keys.</summary>
public sealed record ThemeOption(string Name, Uri Source, Brush Swatch);
