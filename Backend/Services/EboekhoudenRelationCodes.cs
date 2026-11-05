using Backend.Models;

namespace Backend.Services;

/// <summary>
/// e-Boekhouden relation codes are at most 13 characters and must match the invoice Relatie column.
/// A charge-card comment "Name (code)" supplies that code and the Bedrijf/Naam value.
/// </summary>
public static class EboekhoudenRelationCodes
{
    public const int MaxLength = 13;
    public const int NameMaxLength = 100;

    public readonly record struct RelationLabel(string Code, string Name);

    public static ChargeCard? PreferredCard(IEnumerable<ChargeCard?> cards)
    {
        var list = cards.Where(c => c != null).Select(c => c!).OrderBy(c => c.Id).ToList();
        return list.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Street) || !string.IsNullOrWhiteSpace(c.PostalCode))
            ?? list.FirstOrDefault();
    }

    public static IReadOnlyDictionary<int, RelationLabel> Assign(
        IEnumerable<(int Id, string? Name, string? Email, string? Comment)> users)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<int, RelationLabel>();
        foreach (var user in users.OrderBy(u => u.Id))
        {
            var displayName = Source(user.Name, user.Email, user.Id);
            string code;
            string name;
            if (TryParenthetical(user.Comment, out var fromComment, out var fromName) && used.Add(fromComment))
            {
                code = fromComment;
                name = fromName;
            }
            else
            {
                code = Fit(displayName, user.Id, used);
                used.Add(code);
                name = Clip(displayName, NameMaxLength);
            }

            labels[user.Id] = new RelationLabel(code, name);
        }

        return labels;
    }

    public static string ClipName(string? name) => Clip(name ?? string.Empty, NameMaxLength);

    private static bool TryParenthetical(string? comment, out string code, out string name)
    {
        code = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(comment))
            return false;

        var text = comment.Trim();
        if (!text.EndsWith(')'))
            return false;

        var open = text.LastIndexOf('(');
        if (open <= 0)
            return false;

        var inner = text[(open + 1)..^1].Trim();
        if (inner.Length == 0)
            return false;

        code = Clip(inner, MaxLength);
        if (code.Length == inner.Length)
            name = Clip(text, NameMaxLength);
        else
            name = Clip($"{text[..open].TrimEnd()} ({code})", NameMaxLength);

        return code.Length > 0;
    }

    private static string Source(string? name, string? email, int id)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();
        return id.ToString();
    }

    private static string Fit(string source, int userId, HashSet<string> used)
    {
        var plain = Clip(source, MaxLength);
        if (!used.Contains(plain))
            return plain;

        var suffix = userId.ToString();
        if (suffix.Length >= MaxLength)
            return Clip(suffix, MaxLength);

        var prefix = Clip(source, MaxLength - suffix.Length);
        var candidate = prefix + suffix;
        if (!used.Contains(candidate))
            return candidate;

        while (prefix.Length > 0 && used.Contains(candidate))
        {
            prefix = prefix[..^1].TrimEnd();
            candidate = prefix + suffix;
        }

        return used.Contains(candidate) ? Clip(suffix, MaxLength) : candidate;
    }

    private static string Clip(string value, int max)
    {
        var text = value.Trim();
        if (text.Length <= max)
            return text;
        return text[..max].TrimEnd();
    }
}
