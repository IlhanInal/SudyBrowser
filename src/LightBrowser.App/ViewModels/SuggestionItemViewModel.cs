using System.Windows.Media;
using SudyBrowser.Core.Models;

namespace SudyBrowser.App.ViewModels;

/// <summary>
/// Presentation wrapper for one <see cref="Suggestion"/> row in the Omnibox dropdown.
/// Exposes an observable <see cref="Favicon"/> property so when a site's favicon finishes downloading
/// in the background, only this row's icon updates without rebuilding the list or touching keyboard focus.
/// </summary>
public sealed class SuggestionItemViewModel : ObservableObject
{
    private ImageSource? _favicon;

    public SuggestionItemViewModel(Suggestion suggestion, ImageSource? initialFavicon = null)
    {
        Suggestion = suggestion;
        _favicon = initialFavicon;
    }

    public Suggestion Suggestion { get; }

    public string Text => Suggestion.Text;

    public string Title => Suggestion.Title;

    public string Url => Suggestion.Url;

    public string DisplayUrl => Suggestion.DisplayUrl;

    public SuggestionSource Source => Suggestion.Source;

    public bool IsSearch => Suggestion.IsSearch;

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
}
