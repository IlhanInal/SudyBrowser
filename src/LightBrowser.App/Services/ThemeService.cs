using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SudyBrowser.App.Models;

namespace SudyBrowser.App.Services;

/// <summary>Switches the active theme by replacing the theme dictionary (always the first merged dictionary).</summary>
public sealed class ThemeService
{
    private const int ThemeDictionaryIndex = 0;
    private static readonly Uri DarkLogoUri = new("pack://application:,,,/Resources/Icons/sudybrowserlogo.png");
    private static readonly Uri WhiteLogoUri = new("pack://application:,,,/Resources/Icons/sudybrowser_logo_white.png");

    public void Apply(ThemeOption theme)
    {
        if (Application.Current?.Resources is null)
        {
            return;
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = theme.Source };

        // Kullanıcı teması yüklendiğinde veya tema değiştiğinde otomatik parlaklık kontrolü:
        EnsureHomeLogoForTheme(replacement);

        if (dictionaries.Count > ThemeDictionaryIndex)
        {
            dictionaries[ThemeDictionaryIndex] = replacement;
        }
        else
        {
            dictionaries.Insert(ThemeDictionaryIndex, replacement);
        }
    }

    /// <summary>
    /// Eğer temada açıkça HomeLogoSource tanımlı değilse, temanın ana renginin (BarBrush / BoxBrush)
    /// algısal parlaklığını (Luminance) 0 ek bellek maliyetiyle hesaplar:
    /// Koyu/renkli temalarda beyaz logo, çok açık gri/beyaz temalarda koyu logo seçilir.
    /// </summary>
    public static void EnsureHomeLogoForTheme(ResourceDictionary themeDict)
    {
        if (themeDict.Contains("HomeLogoSource"))
        {
            return;
        }

        var isDark = true;
        if (themeDict["BarBrush"] is SolidColorBrush barBrush)
        {
            isDark = IsColorDark(barBrush.Color);
        }
        else if (themeDict["BoxBrush"] is SolidColorBrush boxBrush)
        {
            isDark = IsColorDark(boxBrush.Color);
        }

        themeDict["HomeLogoSource"] = new BitmapImage(isDark ? WhiteLogoUri : DarkLogoUri);
    }

    /// <summary>Standart ITU-R BT.709 algısal parlaklık formülü (0.2126*R + 0.7152*G + 0.0722*B).</summary>
    public static bool IsColorDark(Color c)
    {
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        return luminance < 0.55;
    }
}
