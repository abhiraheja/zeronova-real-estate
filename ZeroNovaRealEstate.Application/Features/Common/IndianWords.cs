using System.Globalization;

namespace ZeroNovaRealEstate.Application.Features.Common;

/// <summary>
/// Rupees in words the Indian way, as agreements write them: "Two Crore Seventeen Lakh Twenty One Thousand Only". Paise
/// are rounded away (agreements carry whole rupees).
/// </summary>
public static class IndianWords
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
        "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    /// <summary>"Rupees Two Lakh Fifty Thousand Only".</summary>
    public static string Rupees(decimal amount) => $"Rupees {Words(amount)} Only";

    /// <summary>"Two Lakh Fifty Thousand" (no "Rupees", no "Only").</summary>
    public static string Words(decimal amount)
    {
        var n = (long)Math.Round(Math.Abs(amount), MidpointRounding.AwayFromZero);
        if (n == 0)
            return "Zero";
        var parts = new List<string>();
        var crore = n / 10_000_000;
        n %= 10_000_000;
        if (crore > 0)
            parts.Add($"{(crore > 99 ? Words(crore) : Below100(crore))} Crore");
        var lakh = n / 100_000;
        n %= 100_000;
        if (lakh > 0)
            parts.Add($"{Below100(lakh)} Lakh");
        var thousand = n / 1000;
        n %= 1000;
        if (thousand > 0)
            parts.Add($"{Below100(thousand)} Thousand");
        var hundred = n / 100;
        n %= 100;
        if (hundred > 0)
            parts.Add($"{Ones[hundred]} Hundred");
        if (n > 0)
            parts.Add(Below100(n));
        return string.Join(' ', parts);
    }

    private static string Below100(long n) => n < 20 ? Ones[n] : n % 10 == 0 ? Tens[n / 10] : $"{Tens[n / 10]} {Ones[n % 10]}";

    /// <summary>"Rs. 2,17,21,000/-" as agreements print it.</summary>
    public static string Figures(decimal amount) => "Rs. " + Group((long)Math.Round(amount, 0, MidpointRounding.AwayFromZero)) + "/-";

    /// <summary>Indian digit grouping: 21721000 → "2,17,21,000" (the last three, then pairs).</summary>
    public static string Group(long n)
    {
        var digits = Math.Abs(n).ToString(CultureInfo.InvariantCulture);
        if (digits.Length > 3)
        {
            var rest = digits[..^3];
            var pairs = new List<string>();
            while (rest.Length > 2)
            {
                pairs.Insert(0, rest[^2..]);
                rest = rest[..^2];
            }
            pairs.Insert(0, rest);
            digits = string.Join(',', pairs) + "," + digits[^3..];
        }
        return n < 0 ? "-" + digits : digits;
    }

    /// <summary>"19th May 2026" from yyyy-MM-dd; the text as it is otherwise.</summary>
    public static string Date(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate) || !DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return isoDate ?? "__________";
        var suffix = (d.Day % 100) is 11 or 12 or 13 ? "th" : (d.Day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{d.Day}{suffix} {d.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"11.05.2026" (payment lines) from yyyy-MM-dd.</summary>
    public static string ShortDate(string? isoDate) =>
        DateOnly.TryParseExact(isoDate ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : "__.__.____";
}
