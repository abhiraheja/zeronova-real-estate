using System.Globalization;
using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Agreements;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Brokerage;

/// <summary>
/// Adds (no id) or changes one side's brokerage on a deal (D13, M4). A rate of zero takes the dealer's default (1% of a
/// sale, a month's rent); GST defaults to 18% when the dealer is GST-registered. Each side gets its own invoice number.
/// </summary>
public sealed record SaveBrokerageCommand(string? BrokerageId, BrokerageInput Input) : IRequest<BrokerageDto>;

public sealed class SaveBrokerageValidator : AbstractValidator<SaveBrokerageCommand>
{
    public SaveBrokerageValidator()
    {
        RuleFor(x => x.Input.DealId).NotEmpty();
        RuleFor(x => x.Input.Rate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Input.Rate).GreaterThan(0).When(x => x.Input.Basis == BrokerageBasis.Fixed).WithMessage("Enter the brokerage amount.");
        RuleFor(x => x.Input.Rate).LessThanOrEqualTo(10).When(x => x.Input.Basis == BrokerageBasis.Percent).WithMessage("A brokerage rate is at most 10%.");
        RuleFor(x => x.Input.Rate).LessThanOrEqualTo(6).When(x => x.Input.Basis == BrokerageBasis.MonthsRent).WithMessage("At most six months' rent.");
        RuleFor(x => x.Input.GstPercent).InclusiveBetween(0, 28).When(x => x.Input.GstPercent is not null);
        RuleFor(x => x.Input.TdsPercent).InclusiveBetween(0, 10).When(x => x.Input.TdsPercent is not null);
        RuleFor(x => x.Input.Notes).MaximumLength(2000);
    }
}

public sealed class SaveBrokerageHandler(IBrokerageRepository brokerages, IDealRepository deals, IPartyRepository parties, IEstateSettingsRepository settings,
    ICounterRepository counters, BrokerageMapper mapper, TimeProvider clock)
    : IRequestHandler<SaveBrokerageCommand, BrokerageDto>
{
    public const string InvoiceCounter = "invoice";

    public async Task<BrokerageDto> Handle(SaveBrokerageCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var deal = await deals.GetDocument(input.DealId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Deal not found.");
        if (deal.Stage == DealStage.Cancelled)
            throw new ValidatorException($"{deal.Code} fell through; no brokerage on it.");
        if (input.Basis == BrokerageBasis.MonthsRent && deal.Kind != PropertyPurpose.Rent)
            throw new ValidatorException("Months of rent is for a rent deal; use a percentage for a sale.");
        if (EstateText.Clean(input.PartyId) is { } partyId)
        {
            if (!deal.SellerIds.Concat(deal.BuyerIds).Contains(partyId) && await parties.GetDocument(partyId).CountAsync(ct) == 0)
                throw new ValidatorException("Party not found.");
        }

        var isNew = string.IsNullOrEmpty(request.BrokerageId);
        var row = isNew
            ? new BrokerageDocument { Id = brokerages.GetNewId, InvoiceNo = await NextInvoiceAsync(ct), DealId = deal.Id, CreatedAt = clock.GetUtcNow().UtcDateTime }
            : await brokerages.GetDocument(request.BrokerageId!).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Brokerage not found.");
        if (!isNew && row.DealId != deal.Id)
            throw new ValidatorException("A brokerage stays on its deal.");
        if (isNew && await brokerages.GetDocuments(b => b.DealId == deal.Id && b.Side == input.Side).AnyAsync(ct))
            throw new ValidatorException($"{deal.Code} already has the {input.Side.ToString().ToLowerInvariant()}'s brokerage. Change that one.");

        var s = await EstateSettings.LoadAsync(settings, ct);
        row.Side = input.Side;
        row.PartyId = EstateText.Clean(input.PartyId) ?? DefaultParty(deal, input.Side);
        row.Basis = input.Basis;
        row.Rate = input.Rate > 0 ? input.Rate : input.Basis == BrokerageBasis.Percent ? s.SaleBrokeragePercent : s.RentBrokerageMonths;
        row.Amount = BrokerageMath.Amount(row.Basis, row.Rate, deal.Price);
        row.GstPercent = input.GstPercent ?? (s.GstRegistered ? 18m : 0m);
        row.TdsPercent = input.TdsPercent ?? 0m;
        row.Notes = EstateText.Clean(input.Notes);
        if (BrokerageMath.Receivable(row) < row.Receipts.Sum(r => r.Amount))
            throw new ValidatorException("More is already received than this brokerage comes to.");

        if (isNew)
            await brokerages.AddAsync(row, ct);
        else
            await brokerages.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }

    /// <summary>"BR/2026-27/0001" — the Indian financial year (April–March) and a running number.</summary>
    private async Task<string> NextInvoiceAsync(CancellationToken ct)
    {
        var today = DateOnly.ParseExact(EstateText.Today(clock), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var start = today.Month >= 4 ? today.Year : today.Year - 1;
        return $"BR/{start}-{(start + 1) % 100:00}/{await counters.NextAsync(InvoiceCounter, ct):0000}";
    }

    private static string? DefaultParty(DealDocument deal, BrokerageSide side) => side switch
    {
        BrokerageSide.Buyer or BrokerageSide.Tenant => deal.BuyerIds.FirstOrDefault(),
        BrokerageSide.Seller or BrokerageSide.Landlord => deal.SellerIds.FirstOrDefault(),
        _ => null,
    };
}

/// <summary>Money received against a brokerage; more than is receivable is refused.</summary>
public sealed record AddBrokerageReceiptCommand(string BrokerageId, BrokerageReceiptInput Input) : IRequest<BrokerageDto>;

public sealed class AddBrokerageReceiptValidator : AbstractValidator<AddBrokerageReceiptCommand>
{
    public AddBrokerageReceiptValidator() => RuleFor(x => x.Input.Amount).GreaterThan(0);
}

public sealed class AddBrokerageReceiptHandler(IBrokerageRepository brokerages, BrokerageMapper mapper, TimeProvider clock)
    : IRequestHandler<AddBrokerageReceiptCommand, BrokerageDto>
{
    public async Task<BrokerageDto> Handle(AddBrokerageReceiptCommand request, CancellationToken ct)
    {
        var row = await brokerages.GetDocument(request.BrokerageId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Brokerage not found.");
        if (row.WrittenOff)
            throw new ValidatorException($"{row.InvoiceNo} is written off.");
        var input = request.Input;
        var due = BrokerageMath.Receivable(row) - row.Receipts.Sum(r => r.Amount);
        if (Math.Round(input.Amount, 2) > due)
            throw new ValidatorException($"Only {EstateText.Rupees(due)} is due on {row.InvoiceNo}.");
        row.Receipts.Add(new BrokerageReceiptModel
        {
            Amount = Math.Round(input.Amount, 2), Mode = input.Mode, Reference = EstateText.Clean(input.Reference),
            Date = EstateText.Date(input.Date, "Date") ?? EstateText.Today(clock),
        });
        await brokerages.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }
}

/// <summary>Gives up on (or brings back) what is still due on a brokerage.</summary>
public sealed record WriteOffBrokerageCommand(string BrokerageId, bool WrittenOff, string? Reason) : IRequest<BrokerageDto>;

public sealed class WriteOffBrokerageHandler(IBrokerageRepository brokerages, BrokerageMapper mapper) : IRequestHandler<WriteOffBrokerageCommand, BrokerageDto>
{
    public async Task<BrokerageDto> Handle(WriteOffBrokerageCommand request, CancellationToken ct)
    {
        var row = await brokerages.GetDocument(request.BrokerageId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Brokerage not found.");
        row.WrittenOff = request.WrittenOff;
        if (request.WrittenOff && EstateText.Clean(request.Reason) is { } reason)
            row.Notes = row.Notes is null ? $"Written off: {reason}" : $"{row.Notes}\nWritten off: {reason}";
        await brokerages.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }
}

public sealed record GetBrokeragesQuery(string? DealId, BrokerageStatus? Status, int Take = 300) : IRequest<IReadOnlyList<BrokerageDto>>;

public sealed class GetBrokeragesHandler(IBrokerageRepository brokerages, BrokerageMapper mapper) : IRequestHandler<GetBrokeragesQuery, IReadOnlyList<BrokerageDto>>
{
    public async Task<IReadOnlyList<BrokerageDto>> Handle(GetBrokeragesQuery request, CancellationToken ct)
    {
        var query = brokerages.GetDocuments();
        if (EstateText.Clean(request.DealId) is { } dealId)
            query = query.Where(b => b.DealId == dealId);
        var rows = await mapper.ManyAsync(await query.OrderByDescending(b => b.Id).Take(Math.Clamp(request.Take, 1, 1000)).ToListAsync(ct), ct);
        return request.Status is { } status ? [.. rows.Where(r => r.Status == status)] : rows;
    }
}

/// <summary>Earned (invoiced, before GST) and received between two dates (yyyy-MM-dd, inclusive); due is everything open now.</summary>
public sealed record GetBrokerageSummaryQuery(string? From, string? To) : IRequest<BrokerageSummaryDto>;

public sealed class GetBrokerageSummaryHandler(IBrokerageRepository brokerages) : IRequestHandler<GetBrokerageSummaryQuery, BrokerageSummaryDto>
{
    public async Task<BrokerageSummaryDto> Handle(GetBrokerageSummaryQuery request, CancellationToken ct)
    {
        var from = EstateText.Date(request.From, "From") ?? "0000-01-01";
        var to = EstateText.Date(request.To, "To") ?? "9999-12-31";
        var rows = await brokerages.GetDocuments().ToListAsync(ct);
        string Day(DateTime at) => new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(330))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool In(string? day) => day is not null && string.CompareOrdinal(day, from) >= 0 && string.CompareOrdinal(day, to) <= 0;

        var earned = rows.Where(r => In(Day(r.CreatedAt))).Sum(r => r.Amount);
        var received = rows.SelectMany(r => r.Receipts).Where(x => In(x.Date)).Sum(x => x.Amount);
        var open = rows.Where(r => !r.WrittenOff).Select(r => BrokerageMath.Receivable(r) - r.Receipts.Sum(x => x.Amount)).Where(d => d > 0).ToList();
        return new BrokerageSummaryDto(earned, received, open.Sum(), open.Count);
    }
}

/// <summary>The brokerage bill as a Word file — the dealer's name and GSTIN, the deal, the amount, GST, and the total in words.</summary>
public sealed record BuildBrokerageInvoiceCommand(string BrokerageId) : IRequest<AgreementFileDto>;

public sealed class BuildBrokerageInvoiceHandler(IBrokerageRepository brokerages, BrokerageBook book) : IRequestHandler<BuildBrokerageInvoiceCommand, AgreementFileDto>
{
    public async Task<AgreementFileDto> Handle(BuildBrokerageInvoiceCommand request, CancellationToken ct)
    {
        var row = await brokerages.GetDocument(request.BrokerageId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Brokerage not found.");
        var (text, warnings) = await book.InvoiceAsync(row, ct);
        return new AgreementFileDto($"{row.InvoiceNo.Replace('/', '-')}.docx", Convert.ToBase64String(AgreementEngine.ToDocx(text)), warnings);
    }
}

/// <summary>The arithmetic of a brokerage: base amount, GST on it, TDS (on the amount before GST), and what the payer hands over.</summary>
public static class BrokerageMath
{
    public static decimal Amount(BrokerageBasis basis, decimal rate, decimal price) => basis switch
    {
        BrokerageBasis.Percent => Math.Round(price * rate / 100m, 0),
        BrokerageBasis.MonthsRent => Math.Round(price * rate, 0),
        _ => Math.Round(rate, 0),
    };

    public static decimal Gst(BrokerageDocument b) => Math.Round(b.Amount * b.GstPercent / 100m, 0);

    public static decimal Tds(BrokerageDocument b) => Math.Round(b.Amount * b.TdsPercent / 100m, 0);

    public static decimal Receivable(BrokerageDocument b) => b.Amount + Gst(b) - Tds(b);
}
