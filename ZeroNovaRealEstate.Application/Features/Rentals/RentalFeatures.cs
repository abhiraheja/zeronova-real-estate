using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Agreements;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Rentals;

/// <summary>
/// Adds (no id) or changes a tenancy (M4). Landlords default to the property's owners (or the rent deal's sellers),
/// tenants to the deal's buyers. A new tenancy marks an available property rented and stops offering it.
/// </summary>
public sealed record SaveTenancyCommand(string? TenancyId, TenancyInput Input) : IRequest<TenancyDto>;

public sealed class SaveTenancyValidator : AbstractValidator<SaveTenancyCommand>
{
    public SaveTenancyValidator()
    {
        RuleFor(x => x.Input.PropertyId).NotEmpty();
        RuleFor(x => x.Input.Rent).GreaterThan(0);
        RuleFor(x => x.Input.Deposit).GreaterThanOrEqualTo(0).When(x => x.Input.Deposit is not null);
        RuleFor(x => x.Input.Maintenance).GreaterThanOrEqualTo(0).When(x => x.Input.Maintenance is not null);
        RuleFor(x => x.Input.StartDate).NotEmpty();
        RuleFor(x => x.Input.Months).InclusiveBetween(1, 120);
        RuleFor(x => x.Input.RentDueDay).InclusiveBetween(1, 28);
        RuleFor(x => x.Input.EscalationPercent).InclusiveBetween(0, 50).When(x => x.Input.EscalationPercent is not null);
        RuleFor(x => x.Input.LockInMonths).InclusiveBetween(0, 120).When(x => x.Input.LockInMonths is not null);
        RuleFor(x => x.Input.NoticeMonths).InclusiveBetween(0, 12).When(x => x.Input.NoticeMonths is not null);
        RuleFor(x => x.Input.Notes).MaximumLength(4000);
    }
}

public sealed class SaveTenancyHandler(ITenancyRepository tenancies, IPropertyRepository properties, IPartyRepository parties, IDealRepository deals,
    MatchEngine engine, TenancyMapper mapper, EstateScope scope)
    : IRequestHandler<SaveTenancyCommand, TenancyDto>
{
    public async Task<TenancyDto> Handle(SaveTenancyCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var property = await properties.GetDocument(input.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var deal = EstateText.Clean(input.DealId) is { } dealId
            ? await deals.GetDocument(dealId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Deal not found.")
            : null;
        if (deal is not null && (deal.PropertyId != property.Id || deal.Kind != PropertyPurpose.Rent))
            throw new ValidatorException($"{deal.Code} is not a rent deal for {property.Code}.");

        var isNew = string.IsNullOrEmpty(request.TenancyId);
        var row = isNew ? null : await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId!, ct);
        if (row is { Status: TenancyStatus.Ended })
            throw new ValidatorException("This tenancy has ended; it cannot change.");
        if (isNew && await tenancies.GetDocuments(t => t.PropertyId == property.Id && t.Status == TenancyStatus.Active).AnyAsync(ct))
            throw new ValidatorException($"{property.Code} already has an active tenancy. End it first.");

        // Empty lists keep what an existing tenancy has (the app shows names only), else take the deal's or the owners.
        var landlords = (input.LandlordIds is { Count: > 0 } l ? l
            : row?.LandlordIds is { Count: > 0 } rl ? rl
            : deal?.SellerIds is { Count: > 0 } ds ? ds
            : property.Owners.Select(o => o.PartyId)).Distinct().ToList();
        var tenants = (input.TenantIds is { Count: > 0 } t ? t : row?.TenantIds is { Count: > 0 } rt ? rt : deal?.BuyerIds ?? []).Distinct().ToList();
        if (tenants.Count == 0)
            throw new ValidatorException("Add the tenant.");
        if (landlords.Intersect(tenants).Any())
            throw new ValidatorException("The same person cannot be both landlord and tenant.");
        var all = landlords.Concat(tenants).ToList();
        if (await parties.GetDocuments(p => all.Contains(p.Id)).CountAsync(ct) != all.Count)
            throw new ValidatorException("A landlord or tenant was not found. Add the person first.");

        var start = EstateText.Date(input.StartDate, "Start date")!;
        var agent = EstateText.Clean(input.AgentId) ?? row?.AgentId ?? deal?.AgentId ?? scope.UserId;
        if (agent != scope.UserId && agent != row?.AgentId && !scope.SeesAllRentals)
            throw new ValidatorException("Only a manager can give a tenancy to another agent.");

        row ??= new TenancyDocument { Id = tenancies.GetNewId, PropertyId = property.Id, StartDate = start, EndDate = start, AgentId = agent };
        row.PropertyId = property.Id;
        row.DealId = deal?.Id ?? row.DealId;
        row.LandlordIds = landlords;
        row.TenantIds = tenants;
        row.Rent = Math.Round(input.Rent, 2);
        row.Deposit = input.Deposit is { } dep ? Math.Round(dep, 2) : null;
        row.Maintenance = input.Maintenance is { } m ? Math.Round(m, 2) : null;
        if (row.StartDate != start || row.Months != input.Months)
            row.RenewalReminded = false;
        row.StartDate = start;
        row.Months = input.Months;
        row.EndDate = Tenancies.EndOf(start, input.Months);
        row.RentDueDay = input.RentDueDay;
        row.EscalationPercent = input.EscalationPercent;
        row.LockInMonths = input.LockInMonths;
        row.NoticeMonths = input.NoticeMonths;
        row.AgentId = agent;
        row.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;

        if (isNew)
        {
            await tenancies.AddAsync(row, ct);
            if (property.Status is PropertyStatus.Available or PropertyStatus.OnHold)
            {
                property.Status = PropertyStatus.Rented;
                property.HoldUntil = null;
                await properties.UpdateAsync(property, ct);
                await engine.DropForPropertyAsync(property.Id, ct);
            }
        }
        else
            await tenancies.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }
}

/// <summary>A month's rent received (yyyy-MM); a month may be paid in parts.</summary>
public sealed record RecordRentPaymentCommand(string TenancyId, RentPaymentInput Input) : IRequest<TenancyDto>;

public sealed class RecordRentPaymentValidator : AbstractValidator<RecordRentPaymentCommand>
{
    public RecordRentPaymentValidator()
    {
        RuleFor(x => x.Input.Amount).GreaterThan(0);
        RuleFor(x => x.Input.Month).Matches(@"^\d{4}-(0[1-9]|1[0-2])$").WithMessage("The month is yyyy-MM.");
    }
}

public sealed class RecordRentPaymentHandler(ITenancyRepository tenancies, TenancyMapper mapper, EstateScope scope, TimeProvider clock)
    : IRequestHandler<RecordRentPaymentCommand, TenancyDto>
{
    public async Task<TenancyDto> Handle(RecordRentPaymentCommand request, CancellationToken ct)
    {
        var row = await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId, ct);
        var input = request.Input;
        if (string.CompareOrdinal(input.Month, row.StartDate[..7]) < 0 || string.CompareOrdinal(input.Month, row.EndDate[..7]) > 0)
            throw new ValidatorException($"{input.Month} is outside the tenancy ({row.StartDate} to {row.EndDate}).");
        row.Payments.Add(new RentPaymentModel
        {
            Month = input.Month, Amount = Math.Round(input.Amount, 2), Mode = input.Mode, Reference = EstateText.Clean(input.Reference),
            Date = EstateText.Date(input.Date, "Date") ?? EstateText.Today(clock),
        });
        row.Payments = [.. row.Payments.OrderBy(p => p.Month).ThenBy(p => p.Date)];
        await tenancies.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }
}

/// <summary>Renews a tenancy from the day after it ends: the new term, and the rent raised by its escalation unless a rent is given.</summary>
public sealed record RenewTenancyCommand(string TenancyId, int Months, decimal? Rent) : IRequest<TenancyDto>;

public sealed class RenewTenancyHandler(ITenancyRepository tenancies, TenancyMapper mapper, EstateScope scope) : IRequestHandler<RenewTenancyCommand, TenancyDto>
{
    public async Task<TenancyDto> Handle(RenewTenancyCommand request, CancellationToken ct)
    {
        var row = await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId, ct);
        if (row.Status == TenancyStatus.Ended)
            throw new ValidatorException("This tenancy has ended. Start a new one.");
        if (request.Months is < 1 or > 120)
            throw new ValidatorException("A term is 1 to 120 months.");
        if (request.Rent is <= 0)
            throw new ValidatorException("The rent must be more than zero.");
        var from = DateOnly.ParseExact(row.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rent = request.Rent ?? Math.Round(row.Rent * (1 + (row.EscalationPercent ?? 0) / 100m), 0);
        row.Notes = (row.Notes is null ? string.Empty : row.Notes + "\n")
            + $"Renewed from {from} for {request.Months} months; rent {EstateText.Rupees(row.Rent)} → {EstateText.Rupees(rent)}.";
        row.StartDate = from;
        row.Months = request.Months;
        row.EndDate = Tenancies.EndOf(from, request.Months);
        row.Rent = rent;
        row.RenewalReminded = false;
        await tenancies.UpdateAsync(row, ct);
        return await mapper.OneAsync(row, ct);
    }
}

/// <summary>Ends a tenancy (the tenant has left): the property is free to let again, confirmed today, and matched.</summary>
public sealed record EndTenancyCommand(string TenancyId, string? Date, string? Reason) : IRequest<TenancyDto>;

public sealed class EndTenancyHandler(ITenancyRepository tenancies, IPropertyRepository properties, MatchEngine engine, TenancyMapper mapper, EstateScope scope,
    TimeProvider clock)
    : IRequestHandler<EndTenancyCommand, TenancyDto>
{
    public async Task<TenancyDto> Handle(EndTenancyCommand request, CancellationToken ct)
    {
        var row = await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId, ct);
        if (row.Status == TenancyStatus.Ended)
            throw new ValidatorException("This tenancy has already ended.");
        var day = EstateText.Date(request.Date, "Date") ?? EstateText.Today(clock);
        row.Status = TenancyStatus.Ended;
        if (string.CompareOrdinal(day, row.EndDate) < 0)
            row.EndDate = day;
        if (EstateText.Clean(request.Reason) is { } reason)
            row.Notes = (row.Notes is null ? string.Empty : row.Notes + "\n") + $"Ended {day}: {reason}";
        await tenancies.UpdateAsync(row, ct);

        if (await properties.GetDocument(row.PropertyId).FirstOrDefaultAsync(ct) is { Status: PropertyStatus.Rented } property)
        {
            property.Status = PropertyStatus.Available;
            property.LastConfirmedAt = clock.GetUtcNow().UtcDateTime;
            await properties.UpdateAsync(property, ct);
            await engine.ForPropertyAsync(property, ct);
        }
        return await mapper.OneAsync(row, ct);
    }
}

public sealed record GetTenanciesQuery(TenancyStatus? Status, string? PropertyId, bool RentDueOnly = false, int Take = 300) : IRequest<IReadOnlyList<TenancyDto>>;

public sealed class GetTenanciesHandler(ITenancyRepository tenancies, TenancyMapper mapper, EstateScope scope) : IRequestHandler<GetTenanciesQuery, IReadOnlyList<TenancyDto>>
{
    public async Task<IReadOnlyList<TenancyDto>> Handle(GetTenanciesQuery request, CancellationToken ct)
    {
        var query = tenancies.GetDocuments();
        if (!scope.SeesAllRentals)
        {
            var me = scope.UserId;
            query = query.Where(t => t.AgentId == me);
        }
        if (request.Status is { } status)
            query = query.Where(t => t.Status == status);
        if (EstateText.Clean(request.PropertyId) is { } propertyId)
            query = query.Where(t => t.PropertyId == propertyId);
        var rows = await mapper.ManyAsync(await query.OrderBy(t => t.EndDate).Take(Math.Clamp(request.Take, 1, 1000)).ToListAsync(ct), ct);
        return request.RentDueOnly ? [.. rows.Where(r => r.RentDueNow)] : rows;
    }
}

public sealed record GetTenancyQuery(string TenancyId) : IRequest<TenancyDto>;

public sealed class GetTenancyHandler(ITenancyRepository tenancies, TenancyMapper mapper, EstateScope scope) : IRequestHandler<GetTenancyQuery, TenancyDto>
{
    public async Task<TenancyDto> Handle(GetTenancyQuery request, CancellationToken ct) =>
        await mapper.OneAsync(await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId, ct), ct);
}

/// <summary>A rent receipt for a month as a Word file (tenants need it for HRA): who paid whom, for which property, how.</summary>
public sealed record BuildRentReceiptCommand(string TenancyId, string Month) : IRequest<AgreementFileDto>;

public sealed class BuildRentReceiptHandler(ITenancyRepository tenancies, IPropertyRepository properties, IPartyRepository parties, EstateScope scope)
    : IRequestHandler<BuildRentReceiptCommand, AgreementFileDto>
{
    public async Task<AgreementFileDto> Handle(BuildRentReceiptCommand request, CancellationToken ct)
    {
        var row = await TenancyLookup.RequireAsync(tenancies, scope, request.TenancyId, ct);
        var paid = row.Payments.Where(p => p.Month == request.Month).ToList();
        if (paid.Count == 0)
            throw new ValidatorException($"No rent is recorded for {request.Month}.");
        var property = await properties.GetDocument(row.PropertyId, true).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var ids = row.LandlordIds.Concat(row.TenantIds).ToList();
        var people = (await parties.GetDocuments(p => ids.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        string Names(IEnumerable<string> list) => string.Join(", ", list.Select(id => people.GetValueOrDefault(id)?.Name ?? AgreementEngine.Blank));

        var total = paid.Sum(p => p.Amount);
        var month = Tenancies.MonthName(request.Month);
        var warnings = new List<string>();
        if (total * 12 > 1_00_000m && row.LandlordIds.All(id => people.GetValueOrDefault(id)?.PanLast4 is null))
            warnings.Add("Rent is over ₹1 lakh a year — the tenant's employer asks for the landlord's PAN for HRA.");
        var lines = new List<string>
        {
            "# RENT RECEIPT",
            $"Received with thanks from {Names(row.TenantIds)} the sum of {IndianWords.Figures(total)} ({IndianWords.Rupees(total)}) as rent for {month} "
                + $"for {AgreementEngine.Describe(property)}.",
        };
        foreach (var p in paid)
            lines.Add($"{IndianWords.Figures(p.Amount)} by {p.Mode}{(p.Reference is { } r ? " " + r : string.Empty)} on {IndianWords.ShortDate(p.Date)}");
        lines.Add(string.Empty);
        lines.Add($"Landlord: {Names(row.LandlordIds)}");
        var pans = row.LandlordIds.Select(id => people.GetValueOrDefault(id)?.PanLast4).OfType<string>().ToList();
        if (pans.Count > 0)
            lines.Add($"Landlord PAN ending: {string.Join(", ", pans)}");
        lines.Add(string.Empty);
        lines.Add("Signature: __________");
        return new AgreementFileDto($"Rent_receipt_{property.Code}_{request.Month}.docx", Convert.ToBase64String(AgreementEngine.ToDocx(string.Join('\n', lines))), warnings);
    }
}

/// <summary>Dates of a tenancy, and whether a month's rent is in.</summary>
public static class Tenancies
{
    /// <summary>The last day of a term: 11 months from 2026-11-01 ends 2027-09-30.</summary>
    public static string EndOf(string start, int months) =>
        DateOnly.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(months).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"November 2026" from 2026-11.</summary>
    public static string MonthName(string month) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : month;

    /// <summary>
    /// The rent for this month is late: the tenancy is running this month, the due day has come, and less than the
    /// rent is recorded for the month.
    /// </summary>
    public static bool RentDue(TenancyDocument t, string today)
    {
        if (t.Status != TenancyStatus.Active || string.CompareOrdinal(today, t.StartDate) < 0 || string.CompareOrdinal(today, t.EndDate) > 0)
            return false;
        var month = today[..7];
        var day = int.Parse(today[8..], CultureInfo.InvariantCulture);
        var dueDay = month == t.StartDate[..7] ? Math.Max(t.RentDueDay, int.Parse(t.StartDate[8..], CultureInfo.InvariantCulture)) : t.RentDueDay;
        return day >= dueDay && t.Payments.Where(p => p.Month == month).Sum(p => p.Amount) < t.Rent;
    }

    public static int DaysToEnd(TenancyDocument t, string today) =>
        DateOnly.ParseExact(t.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber;
}

/// <summary>Tenancies as members see them, with the reminder and renewal messages ready for WhatsApp.</summary>
public sealed class TenancyMapper(IPropertyRepository properties, IPartyRepository parties, IEstateSettingsRepository settings, TimeProvider clock)
{
    public async Task<TenancyDto> OneAsync(TenancyDocument row, CancellationToken ct) => (await ManyAsync([row], ct))[0];

    public async Task<IReadOnlyList<TenancyDto>> ManyAsync(IReadOnlyList<TenancyDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var propertyIds = rows.Select(t => t.PropertyId).Distinct().ToList();
        var props = (await properties.GetDocuments(p => propertyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var partyIds = rows.SelectMany(t => t.LandlordIds.Concat(t.TenantIds)).Distinct().ToList();
        var people = (await parties.GetDocuments(p => partyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var dealer = (await EstateSettings.LoadAsync(settings, ct)).DealerName;
        var today = EstateText.Today(clock);

        return [.. rows.Select(t =>
        {
            var p = props.GetValueOrDefault(t.PropertyId);
            var tenant = t.TenantIds.Select(id => people.GetValueOrDefault(id)).FirstOrDefault(x => x is not null);
            var title = p?.Title ?? "the property";
            var month = Tenancies.MonthName(today[..7]);
            var sign = dealer is null ? string.Empty : $" — {dealer}";
            var reminder = $"Namaste {tenant?.Name ?? "ji"}, the rent of {EstateText.Rupees(t.Rent)} for {month} for {title} was due on the {t.RentDueDay}. "
                + $"Please pay and share the receipt/UTR.{sign}";
            var renewal = $"Namaste {tenant?.Name ?? "ji"}, the rent agreement for {title} ends on {IndianWords.Date(t.EndDate)}. "
                + $"Shall we renew it? Please let us know.{sign}";
            return new TenancyDto(t.Id, t.PropertyId, p?.Code ?? "—", title, t.DealId,
                [.. t.LandlordIds.Select(id => people.GetValueOrDefault(id)?.Name ?? "—")], [.. t.TenantIds.Select(id => people.GetValueOrDefault(id)?.Name ?? "—")],
                tenant?.WhatsApp ?? tenant?.Phone, t.Rent, t.Deposit, t.Maintenance, t.StartDate, t.EndDate, t.Months, t.RentDueDay, t.EscalationPercent,
                t.LockInMonths, t.NoticeMonths, t.Status, [.. t.Payments.Select(x => new RentPaymentDto(x.Month, x.Amount, x.Mode, x.Reference, x.Date))],
                Tenancies.RentDue(t, today), Tenancies.DaysToEnd(t, today), t.AgentId, t.Notes, reminder, renewal);
        })];
    }
}

/// <summary>
/// The rent bells (RentReminderJob runs it): rent past its due day this month, and a tenancy ending
/// within 30 days — each rung once to the agent.
/// </summary>
public sealed class RentReminders(ITenancyRepository tenancies, IPropertyRepository properties, IAgentNotificationProducer bell, TimeProvider clock,
    ILogger<RentReminders> logger)
{
    private const int Page = 200;

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = EstateText.Today(clock);
        var rung = 0;
        for (var skip = 0; ; skip += Page)
        {
            var batch = await tenancies.ActiveAnyTenantAsync(skip, Page, ct);
            foreach (var t in batch)
            {
                var rentDue = Tenancies.RentDue(t, today) && !t.RemindedMonths.Contains(today[..7]);
                var ending = !t.RenewalReminded && Tenancies.DaysToEnd(t, today) is >= 0 and <= 30;
                if (!rentDue && !ending)
                    continue;
                var property = await properties.GetDocument(t.PropertyId, true, t.CompanyId).FirstOrDefaultAsync(ct);
                if (rentDue)
                {
                    t.RemindedMonths.Add(today[..7]);
                    rung += await RingAsync(t, "ESTATE_RENT_DUE", $"Rent due: {property?.Title ?? "a tenancy"}",
                        $"{EstateText.Rupees(t.Rent)} for {Tenancies.MonthName(today[..7])} is not in yet.", now, ct);
                }
                if (ending)
                {
                    t.RenewalReminded = true;
                    rung += await RingAsync(t, "ESTATE_TENANCY_ENDING", $"Tenancy ends {IndianWords.Date(t.EndDate)}",
                        $"{property?.Title ?? "A tenancy"} — renew it or find the next tenant.", now, ct);
                }
                await tenancies.UpdateAsync(t, ct, t.CompanyId);
            }
            if (batch.Count < Page)
                return rung;
        }
    }

    private async Task<int> RingAsync(TenancyDocument t, string kind, string title, string body, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await bell.PublishAsync(new DispatchEventMessage(Guid.NewGuid().ToString(), kind, t.CompanyId, string.Empty,
                new { tenancyId = t.Id }, new DispatchDelivery(false, true, "IfOffline"),
                new DispatchMeta("normal", "zeronova-real-estate", $"{t.Id}:{kind}:{now:yyyyMM}", now),
                new DispatchNotification(t.CompanyId, [t.AgentId], title, body, "/real-estate/rentals", "home")), ct);
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rent bell {Kind} for tenancy {TenancyId} not rung.", kind, t.Id);
            return 0;
        }
    }
}

internal static class TenancyLookup
{
    public static async Task<TenancyDocument> RequireAsync(ITenancyRepository tenancies, EstateScope scope, string id, CancellationToken ct)
    {
        var row = await tenancies.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Tenancy not found.");
        if (!scope.SeesAllRentals && row.AgentId != scope.UserId)
            throw new ValidatorException("This tenancy is another agent's.");
        return row;
    }
}
