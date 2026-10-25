namespace ZeroNovaRealEstate.Models.Parties;

/// <summary>How a person is named in Indian papers: son of, wife of, daughter of, care of.</summary>
public enum RelationKind
{
    SonOf,
    WifeOf,
    DaughterOf,
    CareOf,
}

/// <summary>
/// A person in the dealer's papers — an owner today, a buyer tomorrow (design 4.3). Aadhaar and PAN are sealed on save;
/// empty on an update keeps the stored ones.
/// </summary>
public sealed class PartyInput
{
    public string Name { get; init; } = string.Empty;
    public RelationKind? Relation { get; init; }
    public string? RelativeName { get; init; }
    public string? Phone { get; init; }
    public string? WhatsApp { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? Aadhaar { get; init; }
    public string? Pan { get; init; }

    /// <summary>The lead (zeronova-lead) this person is, when they are one.</summary>
    public string? LeadId { get; init; }
}

/// <summary>A party as members see it: Aadhaar and PAN only as their last four characters; phones only with POWV.</summary>
public sealed record PartyDto(
    string Id,
    string Name,
    RelationKind? Relation,
    string? RelativeName,
    string? Phone,
    string? WhatsApp,
    string? Email,
    string? Address,
    string? AadhaarLast4,
    string? PanLast4,
    string? LeadId,
    IReadOnlyList<string> PropertyIds);
