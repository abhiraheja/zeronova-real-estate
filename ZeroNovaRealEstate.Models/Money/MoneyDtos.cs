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

    /// <summary>TDS the payer deducts (e.g. 2% or 5% under section 194H for a business payer); usually none for a family.</summary>
    public decimal? TdsPercent { get; init; }

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
    DateTimeOffset? CreatedAt);

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
    string RenewalMessage);
