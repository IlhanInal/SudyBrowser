using System.Windows;
using LightBrowser.App.Models;

namespace LightBrowser.App.Services;

/// <summary>Switches the active theme by replacing the theme dictionary (always the first merged dictionary).</summary>
public sealed class ThemeService
{
    private const int ThemeDictionaryIndex = 0;

    public void Apply(ThemeOption theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = theme.Source };
        if (dictionaries.Count > ThemeDictionaryIndex)
        {
            dictionaries[ThemeDictionaryIndex] = replacement;
        }
        else
        {
            dictionaries.Insert(ThemeDictionaryIndex, replacement);
        }
    }
}
