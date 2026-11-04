using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

public interface IAddressParser
{
    /// <summary>Interprets address bar text. Returns <c>null</c> for empty input.</summary>
    AddressInput? Parse(string? text);
}
