using Backend.Models;

namespace Backend.Services;

/// <summary>
/// e-Boekhouden relation codes are typed on the user and are at most 13 characters.
/// The invoice Relatie column uses the same value.
/// </summary>
public static class EboekhoudenRelationCodes
{
    public const int MaxLength = 13;
    public const int NameMaxLength = 100;

    public static ChargeCard? PreferredCard(IEnumerable<ChargeCard?> cards)
    {
        var list = cards.Where(c => c != null).Select(c => c!).OrderBy(c => c.Id).ToList();
        return list.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Street) || !string.IsNullOrWhiteSpace(c.PostalCode))
            ?? list.FirstOrDefault();
    }

    public static string Normalize(string? code) => (code ?? string.Empty).Trim();

    public static string? StoredCode(string? code)
    {
        var text = Normalize(code);
        return text.Length == 0 ? null : text;
    }

    public static string ClipName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        return text.Length <= NameMaxLength ? text : text[..NameMaxLength].TrimEnd();
    }
}
