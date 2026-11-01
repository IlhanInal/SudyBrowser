using LightBrowser.Core.Models;

namespace LightBrowser.Core.Interfaces;

public interface IAddressParser
{
    /// <summary>Interprets address bar text. Returns <c>null</c> for empty input.</summary>
    AddressInput? Parse(string? text);
}
