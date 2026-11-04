using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

public interface IBookmarkStore
{
    IReadOnlyList<BookmarkEntry> Load();

    void Save(IReadOnlyList<BookmarkEntry> bookmarks);
}
