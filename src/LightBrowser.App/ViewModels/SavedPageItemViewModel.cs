using System.Windows.Media;

namespace SudyBrowser.App.ViewModels;

/// <summary>
/// Presentation row for a bookmark or history item inside the Settings popup.
/// Holds the display title, target URL, optional time &amp; day group header (for history),
/// selection state for multi-delete, and a cached 20x20 frozen favicon.
/// </summary>
public sealed class SavedPageItemViewModel : ObservableObject
{
    private readonly Action? _onSelectionChanged;
    private ImageSource? _favicon;
    private bool _isSelected;
    private bool _showDateHeader;

    public SavedPageItemViewModel(
        string url,
        string title,
        string faviconUrl = "",
        string timeText = "",
        ImageSource? initialFavicon = null,
        string dateHeader = "",
        bool showDateHeader = false,
        Action? onSelectionChanged = null)
    {
        Url = url;
        Title = FormatDisplayTitle(title, url);
        FaviconUrl = faviconUrl ?? string.Empty;
        TimeText = timeText ?? string.Empty;
        DateHeader = dateHeader ?? string.Empty;
        _showDateHeader = showDateHeader;
        _favicon = initialFavicon;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Url { get; }

    public string Title { get; }

    public string FaviconUrl { get; }

    public string TimeText { get; }

    public string DateHeader { get; }

    public bool HasTimeText => !string.IsNullOrEmpty(TimeText);

    public bool ShowDateHeader
    {
        get => _showDateHeader;
        set => SetProperty(ref _showDateHeader, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke();
            }
        }
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set
        {
            if (SetProperty(ref _favicon, value))
            {
                OnPropertyChanged(nameof(HasFavicon));
            }
        }
    }

    public bool HasFavicon => _favicon is not null;

    private static string FormatDisplayTitle(string? title, string url)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var trimmed = title.Trim();
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        return string.IsNullOrWhiteSpace(title) ? url : title.Trim();
    }
}
