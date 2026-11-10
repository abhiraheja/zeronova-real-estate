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
/// Where a person lives for tax and FEMA (C5, D38): a non-resident seller is taxed at source under s.195 (not the 1%
/// property TDS); an NRI, OCI or foreign national buyer cannot buy agricultural land or a farmhouse (FEMA).
/// </summary>
public enum PartyResidency
{
    Resident,
    Nri,
    Oci,
    ForeignNational,
}

/// <summary>For stamp duty estimates (C5): several states charge a woman buyer less.</summary>
public enum PartyGender
{
    Male,
    Female,
    Other,
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

    /// <summary>The zeronova-lead contact this person is (C2); empty keeps the stored one for the same lead.</summary>
    public string? ContactId { get; init; }

    /// <summary>Resident, NRI, OCI or foreign national (C5); null keeps the stored value (see <see cref="ClearResidency"/>).</summary>
    public PartyResidency? Residency { get; init; }

    /// <summary>For the stamp duty estimate (C5); null keeps the stored value (see <see cref="ClearGender"/>).</summary>
    public PartyGender? Gender { get; init; }

    /// <summary>True removes the stored residency (back to "not set"); <see cref="Residency"/> is then ignored.</summary>
    public bool? ClearResidency { get; init; }

    /// <summary>True removes the stored gender (back to "not set"); <see cref="Gender"/> is then ignored.</summary>
    public bool? ClearGender { get; init; }
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
    IReadOnlyList<string> PropertyIds,
    string? ContactId = null,
    PartyResidency? Residency = null,
    PartyGender? Gender = null);
