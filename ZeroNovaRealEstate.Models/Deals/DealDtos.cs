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

public sealed record DealPaymentDto(PaymentMode Mode, decimal Amount, string AmountInWords, string? Reference, string? Bank, string? Date, string? PayerPartyId, string? PayerName);

/// <summary>A payment still to come: how, how much, by when.</summary>
public sealed record DealInstalmentInput(PaymentMode? Mode, decimal Amount, string? DueDate);

public sealed record DealInstalmentDto(PaymentMode? Mode, decimal Amount, string AmountInWords, string? DueDate);

public sealed record WitnessInput(string Name, string? Address);

public sealed record WitnessDto(string Name, string? Address);

public sealed record ChecklistItemDto(string Key, string Label, bool Done, string? Date, string? Note);

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

public sealed record DealPartyDto(string Id, string Name, string? Relation, string? RelativeName, string? AadhaarLast4, string? PanLast4, string? Address);

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
    DateTimeOffset? CreatedAt);

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
