using System.Text.RegularExpressions;
using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;

namespace ZeroNovaRealEstate.Application.Features.Settings;

// ── Settings ──────────────────────────────────────────────────────────────────────────────────────────

public sealed record GetEstateSettingsQuery : IRequest<EstateSettingsDto>;

public sealed class GetEstateSettingsHandler(IEstateSettingsRepository settings) : IRequestHandler<GetEstateSettingsQuery, EstateSettingsDto>
{
    public async Task<EstateSettingsDto> Handle(GetEstateSettingsQuery request, CancellationToken ct) =>
        SettingsMapping.ToDto(await EstateSettings.LoadAsync(settings, ct));
}

/// <summary>
/// The dealer's settings. A new local value of a regional unit applies to sizes entered from now on; sizes already saved
/// keep the sq ft they were saved with.
/// </summary>
public sealed record SaveEstateSettingsCommand(EstateSettingsInput Input) : IRequest<EstateSettingsDto>;

public sealed class SaveEstateSettingsValidator : AbstractValidator<SaveEstateSettingsCommand>
{
    public SaveEstateSettingsValidator()
    {
        RuleFor(x => x.Input.MarlaSqFt).InclusiveBetween(100, 1000);
        RuleFor(x => x.Input.KanalSqFt).InclusiveBetween(1000, 20000);
        RuleFor(x => x.Input.BighaSqFt).InclusiveBetween(1000, 100000);
        RuleFor(x => x.Input.BiswaSqFt).InclusiveBetween(100, 10000);
        RuleFor(x => x.Input.KathaSqFt).InclusiveBetween(100, 10000);
        RuleFor(x => x.Input.BudgetTolerancePercent).InclusiveBetween(0, 50);
        RuleFor(x => x.Input.SaleFreshDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Input.RentFreshDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Input.MatchThreshold).InclusiveBetween(0, 100);
        RuleFor(x => x.Input.ReraNumber).MaximumLength(60);
        RuleFor(x => x.Input.SaleBrokeragePercent).InclusiveBetween(0, 10);
        RuleFor(x => x.Input.RentBrokerageMonths).InclusiveBetween(0, 6);
        RuleFor(x => x.Input.Gstin).Matches("^[0-9]{2}[A-Za-z0-9]{13}$").When(x => !string.IsNullOrWhiteSpace(x.Input.Gstin))
            .WithMessage("A GSTIN has 15 characters, starting with the state code.");
    }
}

public sealed class SaveEstateSettingsHandler(IEstateSettingsRepository settings) : IRequestHandler<SaveEstateSettingsCommand, EstateSettingsDto>
{
    public async Task<EstateSettingsDto> Handle(SaveEstateSettingsCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var row = await settings.GetDocuments().FirstOrDefaultAsync(ct);
        var isNew = row is null;
        row ??= new EstateSettingsDocument { Id = settings.GetNewId };
        row.MarlaSqFt = input.MarlaSqFt;
        row.KanalSqFt = input.KanalSqFt;
        row.BighaSqFt = input.BighaSqFt;
        row.BiswaSqFt = input.BiswaSqFt;
        row.KathaSqFt = input.KathaSqFt;
        row.BudgetTolerancePercent = input.BudgetTolerancePercent;
        row.SaleFreshDays = input.SaleFreshDays;
        row.RentFreshDays = input.RentFreshDays;
        row.MatchThreshold = input.MatchThreshold;
        row.DefaultCity = EstateText.Clean(input.DefaultCity);
        row.ReraNumber = EstateText.Clean(input.ReraNumber);
        row.DealerName = EstateText.Clean(input.DealerName);
        row.DealerPhone = EstateText.Phone(input.DealerPhone);
        row.GstRegistered = input.GstRegistered;
        row.Gstin = EstateText.Clean(input.Gstin)?.ToUpperInvariant();
        row.DealerAddress = EstateText.Clean(input.DealerAddress);
        row.SaleBrokeragePercent = input.SaleBrokeragePercent;
        row.RentBrokerageMonths = input.RentBrokerageMonths;
        if (isNew)
            await settings.AddAsync(row, ct);
        else
            await settings.UpdateAsync(row, ct);
        return SettingsMapping.ToDto(row);
    }
}

internal static class SettingsMapping
{
    public static EstateSettingsDto ToDto(EstateSettingsDocument s) => new(s.MarlaSqFt, s.KanalSqFt, s.BighaSqFt, s.BiswaSqFt, s.KathaSqFt,
        s.BudgetTolerancePercent, s.SaleFreshDays, s.RentFreshDays, s.MatchThreshold, s.DefaultCity, s.ReraNumber, s.DealerName, s.DealerPhone,
        s.GstRegistered, s.Gstin, s.DealerAddress, s.SaleBrokeragePercent, s.RentBrokerageMonths);

    public static CustomFieldDto ToDto(CustomFieldDocument f) => new(f.Id, f.Key, f.Label, f.Kind, f.Options, f.PropertyTypes, f.Filterable);
}

// ── The dealer's own fields ───────────────────────────────────────────────────────────────────────────

public sealed record GetCustomFieldsQuery : IRequest<IReadOnlyList<CustomFieldDto>>;

public sealed class GetCustomFieldsHandler(ICustomFieldRepository fields) : IRequestHandler<GetCustomFieldsQuery, IReadOnlyList<CustomFieldDto>>
{
    public async Task<IReadOnlyList<CustomFieldDto>> Handle(GetCustomFieldsQuery request, CancellationToken ct) =>
        [.. (await fields.GetDocuments().OrderBy(f => f.Order).ThenBy(f => f.Label).ToListAsync(ct)).Select(SettingsMapping.ToDto)];
}

/// <summary>Adds (no id) or changes a field. The key comes from the first label and never changes, so saved values keep working.</summary>
public sealed record SaveCustomFieldCommand(string? FieldId, CustomFieldInput Input) : IRequest<CustomFieldDto>;

public sealed class SaveCustomFieldValidator : AbstractValidator<SaveCustomFieldCommand>
{
    public SaveCustomFieldValidator()
    {
        RuleFor(x => x.Input.Label).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Input.Options).Must(o => o is { Count: >= 2 and <= 30 })
            .When(x => x.Input.Kind == CustomFieldKind.Choice).WithMessage("A choice needs 2 to 30 options.");
    }
}

public sealed partial class SaveCustomFieldHandler(ICustomFieldRepository fields) : IRequestHandler<SaveCustomFieldCommand, CustomFieldDto>
{
    public async Task<CustomFieldDto> Handle(SaveCustomFieldCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var label = EstateText.Clean(input.Label)!;
        CustomFieldDocument field;
        if (string.IsNullOrEmpty(request.FieldId))
        {
            var key = NonWord().Replace(label.ToLowerInvariant(), "_").Trim('_');
            if (key.Length == 0)
                throw new ValidatorException("Use letters or digits in the field's name.");
            if (await fields.GetDocuments(f => f.Key == key).AnyAsync(ct))
                throw new ValidatorException($"A field called \"{label}\" already exists.");
            field = new CustomFieldDocument
            {
                Id = fields.GetNewId, Key = key, Label = label, Order = await fields.GetDocuments().CountAsync(ct),
            };
        }
        else
            field = await fields.GetDocument(request.FieldId!).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Field not found.");

        field.Label = label;
        field.Kind = input.Kind;
        field.Options = input.Kind == CustomFieldKind.Choice
            ? [.. (input.Options ?? []).Select(EstateText.Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)]
            : [];
        field.PropertyTypes = [.. (input.PropertyTypes ?? []).Distinct()];
        field.Filterable = input.Filterable && input.Kind is CustomFieldKind.YesNo or CustomFieldKind.Choice;
        if (string.IsNullOrEmpty(request.FieldId))
            await fields.AddAsync(field, ct);
        else
            await fields.UpdateAsync(field, ct);
        return SettingsMapping.ToDto(field);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}

/// <summary>Removes a field from the form; values already saved on properties stay in them, unshown.</summary>
public sealed record DeleteCustomFieldCommand(string FieldId) : IRequest<bool>;

public sealed class DeleteCustomFieldHandler(ICustomFieldRepository fields) : IRequestHandler<DeleteCustomFieldCommand, bool>
{
    public async Task<bool> Handle(DeleteCustomFieldCommand request, CancellationToken ct)
    {
        await fields.DeleteAsync(request.FieldId, ct);
        return true;
    }
}

// ── Localities ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The localities the dealer has used, with how many properties each has in stock, for typing them the same way.</summary>
public sealed record GetLocalitiesQuery(string? Search) : IRequest<IReadOnlyList<LocalityDto>>;

public sealed class GetLocalitiesHandler(ILocalityRepository localities, IPropertyRepository properties) : IRequestHandler<GetLocalitiesQuery, IReadOnlyList<LocalityDto>>
{
    public async Task<IReadOnlyList<LocalityDto>> Handle(GetLocalitiesQuery request, CancellationToken ct)
    {
        var query = localities.GetDocuments();
        if (EstateText.Clean(request.Search) is { } search)
        {
            var key = EstateText.Key(search);
            query = query.Where(l => l.Key.Contains(key));
        }
        var rows = await query.OrderBy(l => l.Name).Take(200).ToListAsync(ct);
        var keys = rows.Select(l => l.Key).ToList();
        var counts = (await properties.GetDocuments(p => keys.Contains(p.LocalityKey)
                && (p.Status == PropertyStatus.Available || p.Status == PropertyStatus.OnHold)).Select(p => p.LocalityKey).ToListAsync(ct))
            .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        return [.. rows.Select(l => new LocalityDto(l.Name, l.City, counts.GetValueOrDefault(l.Key)))];
    }
}

// ── Dashboard ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The Real Estate dashboard: counts over the stock and the caller's customers, the newest matches, the oldest stale properties.</summary>
public sealed record GetEstateDashboardQuery : IRequest<EstateDashboardDto>;

public sealed class GetEstateDashboardHandler(
    IPropertyRepository properties, IRequirementRepository requirements, IMatchRepository matches, IEstateSettingsRepository settings,
    Properties.PropertyMapper mapper, EstateScope scope, TimeProvider clock)
    : IRequestHandler<GetEstateDashboardQuery, EstateDashboardDto>
{
    public async Task<EstateDashboardDto> Handle(GetEstateDashboardQuery request, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var saleCut = now.AddDays(-s.SaleFreshDays);
        var rentCut = now.AddDays(-s.RentFreshDays);
        var weekAgo = now.AddDays(-7);

        var forSale = await properties.GetDocuments(p => p.Status == PropertyStatus.Available && p.Purpose == PropertyPurpose.Sale).CountAsync(ct);
        var forRent = await properties.GetDocuments(p => p.Status == PropertyStatus.Available && p.Purpose == PropertyPurpose.Rent).CountAsync(ct);
        var staleQuery = properties.GetDocuments(p => (p.Status == PropertyStatus.Available || p.Status == PropertyStatus.OnHold)
            && ((p.Purpose == PropertyPurpose.Sale && p.LastConfirmedAt < saleCut) || (p.Purpose == PropertyPurpose.Rent && p.LastConfirmedAt < rentCut)));
        var stale = await staleQuery.CountAsync(ct);
        var oldest = await staleQuery.OrderBy(p => p.LastConfirmedAt).Take(5).ToListAsync(ct);
        var added = (await properties.GetDocuments().Where(p => p.CreatedBy != null).Select(p => p.CreatedBy!.ChangedDate).ToListAsync(ct))
            .Count(d => d >= weekAgo);

        var reqQuery = requirements.GetDocuments(r => r.Status == RequirementStatus.Active);
        var matchQuery = matches.GetDocuments(m => m.Status == MatchStatus.New);
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            reqQuery = reqQuery.Where(r => r.AgentId == me);
            matchQuery = matchQuery.Where(m => m.AgentId == me);
        }
        var active = await reqQuery.CountAsync(ct);
        var newMatches = await matchQuery.CountAsync(ct);
        var latest = await matchQuery.OrderByDescending(m => m.CreatedAt).Take(10).ToListAsync(ct);

        return new EstateDashboardDto(forSale, forRent, stale, active, newMatches, added,
            await MatchMapping.ManyAsync(latest, properties, requirements, ct), await mapper.ManyAsync(oldest, ct));
    }
}
