using System.Windows.Input;
using System.Windows.Media;
using LightBrowser.App.Models;
using LightBrowser.App.Services;

namespace LightBrowser.App.ViewModels;

/// <summary>Temporary settings: home background and theme. Not persisted yet.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string PackPrefix = "pack://application:,,,/Resources/";

    private readonly ThemeService _themeService;
    private BackgroundOption _selectedBackground;
    private ThemeOption _selectedTheme;
    private bool _isOpen;

    public SettingsViewModel(ThemeService themeService)
    {
        _themeService = themeService;

        Backgrounds = new[]
        {
            Background("Yeşil Tepeler", "mountain"),
            Background("Nehir Vadisi", "coast"),
            Background("Orman ve Deniz", "forest"),
            Background("Karlı Dağlar", "lake"),
            Background("Kış Kampı", "snow"),
            Background("Fiyort", "valley"),
        };
        Themes = new[]
        {
            Theme("Açık gri", "Light", "#D9D9D9"),
            Theme("Renkli (mor)", "Colorful", "#8E73E8"),
            Theme("Kırmızı", "red", "#ce3c37"),
            Theme("Dark", "dark", "#070707"),
        };

        _selectedBackground = Backgrounds[0];
        _selectedTheme = Themes[0];

        OpenCommand = new RelayCommand(_ => IsOpen = true);
    }

    public IReadOnlyList<BackgroundOption> Backgrounds { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public ICommand OpenCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public BackgroundOption SelectedBackground
    {
        get => _selectedBackground;
        set
        {
            // ListBox pushes null while its items are being rebuilt; keep the current choice.
            if (value is not null)
            {
                SetProperty(ref _selectedBackground, value);
            }
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && SetProperty(ref _selectedTheme, value))
            {
                _themeService.Apply(value);
            }
        }
    }

    private static BackgroundOption Background(string name, string file) => new(
        name,
        new Uri($"{PackPrefix}Backgrounds/{file}.jpg"),
        new Uri($"{PackPrefix}Backgrounds/{file}_thumb.jpg"));

    private static ThemeOption Theme(string name, string file, string swatchColor)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(swatchColor)!;
        brush.Freeze();
        return new ThemeOption(name, new Uri($"{PackPrefix}Themes/{file}.xaml"), brush);
    }
}
