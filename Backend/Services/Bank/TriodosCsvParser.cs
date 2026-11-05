using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Services.Bank;

public sealed record TriodosTransaction(
    DateOnly BookedOn,
    string AccountIban,
    decimal Amount,
    string Direction,
    string CounterpartyName,
    string? CounterpartyBic,
    string? CounterpartyIban,
    string? TransactionCode,
    string Description,
    decimal? BalanceAfter,
    string Fingerprint);

public sealed record TriodosParseResult(IReadOnlyList<TriodosTransaction> Transactions, int Skipped);

/// <summary>
/// Headerless Triodos internet-banking CSV. Nine quoted columns:
/// date, own IBAN, amount, Credit/Debit, name, BIC + IBAN, code, description, balance.
/// </summary>
public static class TriodosCsvParser
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly string[] DateFormats = ["dd-MM-yyyy", "d-M-yyyy"];

    public static TriodosParseResult Parse(string text)
    {
        var transactions = new List<TriodosTransaction>();
        var skipped = 0;
        foreach (var fields in SplitRows(text))
        {
            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;
            if (TryMap(fields, out var transaction))
                transactions.Add(transaction);
            else
                skipped++;
        }

        return new TriodosParseResult(transactions, skipped);
    }

    public static string Fingerprint(DateOnly bookedOn, decimal amount, string? counterpartyIban, string description)
    {
        var iban = (counterpartyIban ?? string.Empty).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var raw = string.Join('|',
            bookedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            iban,
            description.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool TryMap(IReadOnlyList<string> fields, out TriodosTransaction transaction)
    {
        transaction = null!;
        if (fields.Count != 9)
            return false;
        if (!DateOnly.TryParseExact(fields[0].Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bookedOn))
            return false;
        if (!decimal.TryParse(fields[2].Trim(), NumberStyles.Number, Dutch, out var amount))
            return false;

        var direction = fields[3].Trim();
        if (direction.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            direction = "Credit";
        else if (direction.Equals("Debit", StringComparison.OrdinalIgnoreCase))
            direction = "Debit";
        else
            return false;

        decimal? balance = null;
        if (decimal.TryParse(fields[8].Trim(), NumberStyles.Number, Dutch, out var parsedBalance))
            balance = parsedBalance;

        var (bic, iban) = SplitBank(fields[5]);
        var description = fields[7].Trim();
        var account = fields[1].Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        transaction = new TriodosTransaction(
            bookedOn,
            account,
            amount,
            direction,
            fields[4].Trim(),
            bic,
            iban,
            string.IsNullOrWhiteSpace(fields[6]) ? null : fields[6].Trim(),
            description,
            balance,
            Fingerprint(bookedOn, amount, iban, description));
        return true;
    }

    private static (string? Bic, string? Iban) SplitBank(string raw)
    {
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (null, null);

        var iban = parts.LastOrDefault(LooksLikeIban);
        if (iban == null)
            return (string.Join(' ', parts), null);

        var bic = string.Join(' ', parts.Where(part => !string.Equals(part, iban, StringComparison.OrdinalIgnoreCase)));
        return (string.IsNullOrEmpty(bic) ? null : bic, iban.ToUpperInvariant());
    }

    private static bool LooksLikeIban(string value) =>
        value.Length is >= 15 and <= 34
        && char.IsLetter(value[0])
        && char.IsLetter(value[1])
        && char.IsDigit(value[2]);

    internal static List<List<string>> SplitRows(string text)
    {
        var rows = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                if (fields.Any(part => part.Length > 0))
                    rows.Add(fields);
                fields = new List<string>();
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (fields.Any(part => part.Length > 0))
                rows.Add(fields);
        }

        return rows;
    }
}
