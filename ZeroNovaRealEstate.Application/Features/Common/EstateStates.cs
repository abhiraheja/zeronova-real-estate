using ZeroNovaRealEstate.Domain.Documents;

namespace ZeroNovaRealEstate.Application.Features.Common;

/// <summary>
/// India's states and union territories (C5): the law that applies to a deal (TDS is national; stamp duty, brokerage
/// caps and tenancy rules are the state's). A known name is kept in its usual spelling; anything else as typed.
/// </summary>
public static class EstateStates
{
    public const string Punjab = "Punjab";
    public const string Haryana = "Haryana";
    public const string Chandigarh = "Chandigarh";

    public static readonly IReadOnlyList<string> All =
    [
        Punjab, Haryana, Chandigarh, "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh", "Goa", "Gujarat", "Himachal Pradesh",
        "Jharkhand", "Karnataka", "Kerala", "Madhya Pradesh", "Maharashtra", "Manipur", "Meghalaya", "Mizoram", "Nagaland", "Odisha", "Rajasthan",
        "Sikkim", "Tamil Nadu", "Telangana", "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal", "Andaman and Nicobar Islands",
        "Dadra and Nagar Haveli and Daman and Diu", "Delhi", "Jammu and Kashmir", "Ladakh", "Lakshadweep", "Puducherry",
    ];

    /// <summary>"haryana " → "Haryana"; "UT Chandigarh" → "Chandigarh"; an unknown name as typed; empty → null.</summary>
    public static string? Normalize(string? state)
    {
        var text = EstateText.Clean(state);
        if (text is null)
            return null;
        var bare = text.Replace("UT of ", "", StringComparison.OrdinalIgnoreCase).Replace("UT ", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" UT", "", StringComparison.OrdinalIgnoreCase).Trim();
        return All.FirstOrDefault(s => string.Equals(s, bare, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(s => string.Equals(s, text, StringComparison.OrdinalIgnoreCase))
            ?? text;
    }

    /// <summary>A property's state: its own, else the company's default.</summary>
    public static string? Of(PropertyDocument property, EstateSettingsDocument settings) => Normalize(property.State) ?? Normalize(settings.DefaultState);

    public static bool Is(string? state, string name) => string.Equals(Normalize(state), name, StringComparison.OrdinalIgnoreCase);
}
