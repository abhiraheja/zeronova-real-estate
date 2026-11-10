using ZeroNovaRealEstate.Models.Deals;

namespace ZeroNovaRealEstate.Models.Money;

/// <summary>Whose brokerage it is (D13): each side of a deal pays its own.</summary>
public enum BrokerageSide
{
    Buyer,
    Seller,
    Tenant,
    Landlord,
    Other,
}

/// <summary>How the brokerage is reckoned: a percentage of the price, months of rent, or a fixed amount.</summary>
public enum BrokerageBasis
{
    Percent,
    MonthsRent,
    Fixed,
}

/// <summary>
/// Who pays a side's brokerage, for TDS (C5): an individual or HUF generally deducts none; a company, firm or other
/// business deducts 2% — 20% when the dealer (the payee) has no PAN on record — once its brokerage to the dealer in
/// the financial year is over ₹20,000.
/// </summary>
public enum PayerKind
{
    IndividualOrHuf,
    CompanyOrFirm,
}

public enum BrokerageStatus
{
    Due,
    PartlyReceived,
    Received,
    WrittenOff,
}

public sealed class BrokerageInput
{
    public string DealId { get; init; } = string.Empty;
    public BrokerageSide Side { get; init; }
    public string? PartyId { get; init; }
    public BrokerageBasis Basis { get; init; }

    /// <summary>The percentage, the months of rent, or the amount — by <see cref="Basis"/>.</summary>
    public decimal Rate { get; init; }

    /// <summary>Null = the dealer's setting (18% when GST-registered, else none).</summary>
    public decimal? GstPercent { get; init; }

    /// <summary>
    /// TDS the payer deducts, to override the rule. Null = the rule (C5): none for an individual or HUF; 2% (20% without
    /// the dealer's PAN) for a company or firm once its brokerage in the year is over ₹20,000.
    /// </summary>
    public decimal? TdsPercent { get; init; }

    /// <summary>Who pays (C5). Null keeps the stored kind; a new brokerage defaults to an individual or HUF.</summary>
    public PayerKind? PayerKind { get; init; }

    public string? Notes { get; init; }
}

public sealed record BrokerageReceiptInput(decimal Amount, PaymentMode Mode, string? Reference, string? Date);

public sealed record BrokerageReceiptDto(decimal Amount, PaymentMode Mode, string? Reference, string? Date);

public sealed record BrokerageDto(
    string Id,
    string InvoiceNo,
    string DealId,
    string DealCode,
    string PropertyCode,
    string PropertyTitle,
    BrokerageSide Side,
    string? PartyId,
    string? PartyName,
    BrokerageBasis Basis,
    decimal Rate,
    decimal Amount,
    decimal GstPercent,
    decimal Gst,
    decimal Total,
    decimal TdsPercent,
    decimal Tds,
    decimal Receivable,
    IReadOnlyList<BrokerageReceiptDto> Receipts,
    decimal Received,
    decimal Due,
    BrokerageStatus Status,
    string? Notes,
    DateTimeOffset? CreatedAt)
{
    /// <summary>Who pays (C5); null on a brokerage saved before C5.</summary>
    public PayerKind? PayerKind { get; init; }

    /// <summary>The TDS section by the invoice date (C5): s.393(1) (Table Sl. 1(ii)) from 1 April 2026, s.194H before; null with no TDS.</summary>
    public string? TdsSection { get; init; }

    /// <summary>Why TDS is or is not shown (C5).</summary>
    public string? TdsNote { get; init; }

    /// <summary>What to check (C5): Haryana's 1% brokerage cap, for one.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Brokerage over a period: earned (invoiced), received, and what is still due overall.</summary>
public sealed record BrokerageSummaryDto(decimal Earned, decimal Received, decimal DueNow, int OpenInvoices);

// ── Rentals ───────────────────────────────────────────────────────────────────────────────────────────

public enum TenancyStatus
{
    Active,
    Ended,
}

public sealed class TenancyInput
{
    public string PropertyId { get; init; } = string.Empty;
    public string? DealId { get; init; }
    public IReadOnlyList<string> LandlordIds { get; init; } = [];
    public IReadOnlyList<string> TenantIds { get; init; } = [];
    public decimal Rent { get; init; }
    public decimal? Deposit { get; init; }
    public decimal? Maintenance { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string StartDate { get; init; } = string.Empty;

    public int Months { get; init; } = 11;

    /// <summary>The day of the month the rent is due (1–28).</summary>
    public int RentDueDay { get; init; } = 7;

    public decimal? EscalationPercent { get; init; }
    public int? LockInMonths { get; init; }
    public int? NoticeMonths { get; init; }
    public string? AgentId { get; init; }
    public string? Notes { get; init; }

    // ── C5. Null keeps what is saved (an app that does not know them changes nothing); empty text clears.

    /// <summary>yyyy-MM-dd the agreement was signed — Chandigarh's Rent Authority intimation is due 2 months after. Null = the deal's date, else the start.</summary>
    public string? AgreementDate { get; init; }

    /// <summary>The fit-out / rent-free period at the start, in days (a commercial lease); 0 clears.</summary>
    public int? RentFreeDays { get; init; }

    /// <summary>What the premises may be used for (the lease's use clause), e.g. "a retail showroom for garments".</summary>
    public string? PermittedUse { get; init; }
}

/// <summary>
/// A tenancy's paperwork after signing (C5, D38): the tenant's police verification (needed everywhere — a DM or
/// Commissioner's order under s.163 BNSS), and in Chandigarh the Rent Authority intimation with its UIN. Saved as a whole.
/// </summary>
public sealed class TenancyComplianceInput
{
    public bool PoliceVerified { get; init; }

    /// <summary>yyyy-MM-dd.</summary>
    public string? PoliceVerifiedOn { get; init; }

    public string? PoliceStation { get; init; }

    /// <summary>The police reference or Sampark receipt number.</summary>
    public string? PoliceReference { get; init; }

    /// <summary>The verification form or receipt (uploaded like a photo, through zeronova-document-helper).</summary>
    public Properties.PhotoInput? PoliceDocument { get; init; }

    /// <summary>yyyy-MM-dd the tenancy was intimated to Chandigarh's Rent Authority.</summary>
    public string? RentAuthorityDoneOn { get; init; }

    /// <summary>The UIN on the Rent Authority's e-receipt.</summary>
    public string? RentAuthorityUin { get; init; }
}

/// <summary>
/// A tenancy's paperwork (C5). <see cref="PoliceVerificationDue"/>: the tenancy has started and the tenant is not
/// verified. <see cref="RentAuthorityRequired"/>: the property is in Chandigarh — the intimation is due by
/// <see cref="RentAuthorityDueDate"/> (the agreement + 2 months). <see cref="Warnings"/>: what is missing, in words.
/// </summary>
public sealed record TenancyComplianceDto(
    bool PoliceVerified,
    string? PoliceVerifiedOn,
    string? PoliceStation,
    string? PoliceReference,
    Properties.PhotoDto? PoliceDocument,
    bool PoliceVerificationDue,
    bool RentAuthorityRequired,
    string? RentAuthorityDueDate,
    string? RentAuthorityDoneOn,
    string? RentAuthorityUin,
    bool RentAuthorityOverdue,
    IReadOnlyList<string> Warnings)
{
    public static readonly TenancyComplianceDto None = new(false, null, null, null, null, false, false, null, null, null, false, []);
}

public sealed record RentPaymentInput(string Month, decimal Amount, PaymentMode Mode, string? Reference, string? Date);

public sealed record RentPaymentDto(string Month, decimal Amount, PaymentMode Mode, string? Reference, string? Date);

public sealed record TenancyDto(
    string Id,
    string PropertyId,
    string PropertyCode,
    string PropertyTitle,
    string? DealId,
    IReadOnlyList<string> LandlordNames,
    IReadOnlyList<string> TenantNames,
    string? TenantPhone,
    decimal Rent,
    decimal? Deposit,
    decimal? Maintenance,
    string StartDate,
    string EndDate,
    int Months,
    int RentDueDay,
    decimal? EscalationPercent,
    int? LockInMonths,
    int? NoticeMonths,
    TenancyStatus Status,
    IReadOnlyList<RentPaymentDto> Payments,
    bool RentDueNow,
    int DaysToEnd,
    string AgentId,
    string? Notes,
    string RentReminder,
    string RenewalMessage)
{
    /// <summary>yyyy-MM-dd the agreement was signed (C5); null = not entered (the deal's date or the start is used).</summary>
    public string? AgreementDate { get; init; }

    /// <summary>The fit-out / rent-free period in days (C5).</summary>
    public int? RentFreeDays { get; init; }

    /// <summary>The use clause (C5).</summary>
    public string? PermittedUse { get; init; }

    /// <summary>Police verification and, in Chandigarh, the Rent Authority intimation (C5).</summary>
    public TenancyComplianceDto Compliance { get; init; } = TenancyComplianceDto.None;
}
