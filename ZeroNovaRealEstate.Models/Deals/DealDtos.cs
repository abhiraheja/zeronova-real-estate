using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Models.Deals;

/// <summary>Where a deal stands (M3). Completed moves the ownership (sale) and closes the customer's requirement.</summary>
public enum DealStage
{
    Offer,
    Token,
    Agreement,
    Transfer,
    Registry,
    Completed,
    Cancelled,
}

/// <summary>How the property changes hands — it sets the transfer checklist (design 10.10).</summary>
public enum TransferKind
{
    /// <summary>A builder flat before conveyance: the builder transfers the allotment.</summary>
    BuilderAllotment,

    /// <summary>An authority plot or house (e.g. GMADA): NDC, NOC, registry, re-transfer.</summary>
    AuthorityPlot,

    /// <summary>A registered property: sale deed, then mutation.</summary>
    Registered,

    /// <summary>A tenancy: the rent agreement, police verification, keys.</summary>
    Rent,

    /// <summary>A GMADA letter of intent sold before allotment (C5): NDC, then GMADA's LOI transfer (fee about 2.5% of the allotment price).</summary>
    GmadaLoi,

    /// <summary>A Chandigarh leasehold property (C5): the Estate Office's transfer permission and the unearned increase before the sale deed.</summary>
    ChandigarhLeasehold,

    /// <summary>A Chandigarh freehold property (C5): the seller's conveyance deed from the Estate Office, then the sale deed.</summary>
    ChandigarhFreehold,
}

public enum PaymentMode
{
    Cash,
    Cheque,
    DemandDraft,
    Neft,
    Rtgs,
    Imps,
    Upi,
}

/// <summary>A payment made: how, how much, the UTR or cheque number, the bank, the date and who paid.</summary>
public sealed record DealPaymentInput(PaymentMode Mode, decimal Amount, string? Reference, string? Bank, string? Date, string? PayerPartyId);

/// <summary>
/// A payment made. <see cref="Tds"/> (C5): the property TDS the buyer deducts from it (every instalment, token
/// included) under <see cref="TdsSection"/> — the section by the payment's date; null when none is due or it cannot be
/// worked out (a non-resident seller's share, s.195).
/// </summary>
public sealed record DealPaymentDto(PaymentMode Mode, decimal Amount, string AmountInWords, string? Reference, string? Bank, string? Date, string? PayerPartyId, string? PayerName,
    decimal? Tds = null, string? TdsSection = null);

/// <summary>A payment still to come: how, how much, by when.</summary>
public sealed record DealInstalmentInput(PaymentMode? Mode, decimal Amount, string? DueDate);

/// <summary>An instalment to come, with the TDS to deduct from it (C5) as on <see cref="DealPaymentDto"/>.</summary>
public sealed record DealInstalmentDto(PaymentMode? Mode, decimal Amount, string AmountInWords, string? DueDate, decimal? Tds = null, string? TdsSection = null);

public sealed record WitnessInput(string Name, string? Address);

public sealed record WitnessDto(string Name, string? Address);

/// <summary>
/// A transfer step. C5: <see cref="Step"/> is its place in an ordered procedure (GMADA: NDC → sale permission →
/// registry → re-transfer → mutation; null = any time), <see cref="TypicalDays"/> how long it usually takes and
/// <see cref="Hint"/> what to know (a fee, who issues it) — hints, not deadlines.
/// </summary>
public sealed record ChecklistItemDto(string Key, string Label, bool Done, string? Date, string? Note, int? Step = null, int? TypicalDays = null, string? Hint = null);

public sealed record ChecklistUpdateInput(string Key, bool Done, string? Date, string? Note);

/// <summary>
/// A deal on a property (M3): its sellers (the current owners by default) and buyers — several on each side — the price,
/// the payments made and the balance schedule, how it transfers, the agreement's details (place, date, witnesses,
/// extra terms).
/// </summary>
public sealed class DealInput
{
    public string PropertyId { get; init; } = string.Empty;

    /// <summary>Empty = the property's current owners.</summary>
    public IReadOnlyList<string>? SellerIds { get; init; }

    public IReadOnlyList<string> BuyerIds { get; init; } = [];
    public string? RequirementId { get; init; }
    public string? LeadId { get; init; }
    public string? AgentId { get; init; }
    public decimal Price { get; init; }

    /// <summary>The earnest money (biana) as a share of the price, e.g. 10.</summary>
    public decimal? EarnestPercent { get; init; }

    public IReadOnlyList<DealPaymentInput>? Payments { get; init; }
    public IReadOnlyList<DealInstalmentInput>? Schedule { get; init; }

    /// <summary>yyyy-MM-dd: the last date for the full payment and the transfer.</summary>
    public string? FinalDate { get; init; }

    public TransferKind? TransferKind { get; init; }
    public string? ExecutionPlace { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? ExecutionDate { get; init; }

    public IReadOnlyList<WitnessInput>? Witnesses { get; init; }

    /// <summary>The property as the agreement describes it; empty = made from the property.</summary>
    public string? PropertyDescription { get; init; }

    /// <summary>How the seller holds it, e.g. "Allotment Letter dated 26.09.2024 issued by M/s MB Infrabuild Pvt. Ltd."</summary>
    public string? TitleNote { get; init; }

    /// <summary>What goes with the property and who pays what (parking, club membership, transfer charges…), one per line.</summary>
    public IReadOnlyList<string>? Inclusions { get; init; }

    /// <summary>Extra clauses for the agreement, one per item.</summary>
    public IReadOnlyList<string>? Terms { get; init; }

    public string? Notes { get; init; }
}

public sealed record DealPartyDto(string Id, string Name, string? Relation, string? RelativeName, string? AadhaarLast4, string? PanLast4, string? Address,
    Parties.PartyResidency? Residency = null, Parties.PartyGender? Gender = null);

/// <summary>One seller's part of the property TDS (C5): their share, and the rate on it (1%, 20% without a PAN; none worked out for a non-resident).</summary>
public sealed record DealTdsSellerDto(string PartyId, string Name, decimal SharePercent, bool NonResident, bool NoPan, decimal? RatePercent);

/// <summary>
/// The property TDS on a sale (C5, D38). <see cref="Applies"/>: the whole deal (every buyer and seller together) is ₹50
/// lakh or more, or a seller is non-resident (s.195 has no threshold). <see cref="Section"/>: s.393(1) of the Income-tax
/// Act 2025 for an agreement on or after 1 April 2026, s.194-IA before. <see cref="RatePercent"/>: the blended rate over
/// the whole price — Σ each resident seller's share × their rate (1%, or 20% without a PAN), so a resident 50% seller and
/// an NRI 50% seller is 0.5%; never one seller's rate (those are in <see cref="Sellers"/>); null when only non-resident
/// sellers. <see cref="OnFullPrice"/>: the TDS on the whole price in all (each resident seller's part, summed).
/// <see cref="Note"/>: what the buyer does.
/// </summary>
public sealed record DealTdsDto(
    bool Applies,
    decimal Threshold,
    string Section,
    decimal? RatePercent,
    decimal? OnFullPrice,
    bool NonResidentSeller,
    string? Note,
    IReadOnlyList<DealTdsSellerDto> Sellers);

/// <summary>
/// A stamp duty estimate (C5) — an estimate only, from the dealer's rates in the settings: the property's state (and
/// urban or rural), the buyers' genders (all women, all men, or joint), on the deal price. Null rates = none set for the state.
/// </summary>
public sealed record StampDutyEstimateDto(
    string Label,
    string? State,
    bool Rural,
    string Category,
    decimal? DutyPercent,
    decimal? Duty,
    decimal? RegistrationPercent,
    decimal? Registration,
    decimal? Total,
    IReadOnlyList<string> Notes);

public sealed record DealDto(
    string Id,
    string Code,
    string PropertyId,
    string PropertyCode,
    string PropertyTitle,
    PropertyPurpose Kind,
    DealStage Stage,
    IReadOnlyList<DealPartyDto> Sellers,
    IReadOnlyList<DealPartyDto> Buyers,
    string? RequirementId,
    string? LeadId,
    string AgentId,
    decimal Price,
    string PriceInWords,
    decimal? EarnestPercent,
    decimal? EarnestAmount,
    IReadOnlyList<DealPaymentDto> Payments,
    decimal Paid,
    IReadOnlyList<DealInstalmentDto> Schedule,
    decimal Balance,
    string? FinalDate,
    TransferKind TransferKind,
    IReadOnlyList<ChecklistItemDto> Checklist,
    string? ExecutionPlace,
    string? ExecutionDate,
    IReadOnlyList<WitnessDto> Witnesses,
    string? PropertyDescription,
    string? TitleNote,
    IReadOnlyList<string> Inclusions,
    IReadOnlyList<string> Terms,
    string? Notes,
    decimal? Tds,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    DateTimeOffset? CreatedAt)
{
    /// <summary>The property's state or UT (C5): its own, else the company's default.</summary>
    public string? State { get; init; }

    /// <summary>The property TDS in detail (C5); null on a rent deal.</summary>
    public DealTdsDto? TdsDetail { get; init; }

    /// <summary>The stamp duty estimate (C5); null on a rent deal.</summary>
    public StampDutyEstimateDto? StampDuty { get; init; }
}

public sealed record DealStageInput(DealStage Stage, string? Reason);

/// <summary>An earlier change of owners entered by hand, as far back as the papers go (D8).</summary>
public sealed class OwnershipRecordInput
{
    public IReadOnlyList<string>? FromNames { get; init; }
    public IReadOnlyList<string>? ToNames { get; init; }
    public TransferKind Kind { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? Date { get; init; }

    public string? Note { get; init; }
}

public sealed record OwnershipRecordDto(IReadOnlyList<string> From, IReadOnlyList<string> To, TransferKind Kind, string? Date, string? Note, string? DealId);

// ── Agreements ────────────────────────────────────────────────────────────────────────────────────────

public enum AgreementKind
{
    AgreementToSell,
    TokenReceipt,
    RentAgreement,
    Other,

    /// <summary>A commercial lease (C5, D37): lock-in, yearly escalation, fit-out period, security deposit.</summary>
    CommercialLease,

    /// <summary>A residential lease over 11 months (C5, D37), to be registered and stamped.</summary>
    RegisteredLease,
}

/// <summary>
/// A template (D12): a ready one (the owner's Format A and Format B, a token receipt, a rent agreement) or the dealer's
/// own Word file with <c>{{placeholders}}</c>. <see cref="Placeholders"/> lists what an uploaded file asks for.
/// </summary>
public sealed record AgreementTemplateDto(string Id, string Name, AgreementKind Kind, bool BuiltIn, IReadOnlyList<string> Placeholders);

public sealed record AgreementTemplateInput(string Name, AgreementKind Kind, string DocxBase64);

/// <summary>The agreement as text to read and change before it becomes Word, and what to check first.</summary>
public sealed record AgreementPreviewDto(string TemplateId, string Title, string Text, bool Editable, IReadOnlyList<string> Warnings);

/// <summary>The Word file (legal size), base64, with its name.</summary>
public sealed record AgreementFileDto(string FileName, string DocxBase64, IReadOnlyList<string> Warnings);
