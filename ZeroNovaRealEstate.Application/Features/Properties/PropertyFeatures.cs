using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Properties;

// ── Read ──────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The search (design 4.7) over the dealer's whole stock.</summary>
public sealed record GetPropertiesQuery(PropertySearchInput Search) : IRequest<PropertyPageDto>;

public sealed class GetPropertiesHandler(IPropertyRepository properties, IEstateSettingsRepository settings, PropertyMapper mapper, TimeProvider clock)
    : IRequestHandler<GetPropertiesQuery, PropertyPageDto>
{
    public async Task<PropertyPageDto> Handle(GetPropertiesQuery request, CancellationToken ct)
    {
        var f = request.Search;
        var query = properties.GetDocuments();
        var statuses = f.Statuses is { Count: > 0 } st ? st.ToList() : [PropertyStatus.Available, PropertyStatus.OnHold];
        query = query.Where(p => statuses.Contains(p.Status));
        if (f.Purpose is { } purpose)
            query = query.Where(p => p.Purpose == purpose);
        if (f.Types is { Count: > 0 } types)
        {
            var list = types.ToList();
            query = query.Where(p => list.Contains(p.Type));
        }
        if (EstateText.Clean(f.City) is { } city)
            query = query.Where(p => p.City == city);
        if (f.Localities is { Count: > 0 } localities)
        {
            var keys = localities.Select(EstateText.Key).ToList();
            query = query.Where(p => keys.Contains(p.LocalityKey));
        }
        if (f.PriceMin is { } priceMin)
            query = query.Where(p => p.PriceValue >= priceMin);
        if (f.PriceMax is { } priceMax)
            query = query.Where(p => p.PriceValue <= priceMax);
        if (f.SizeMinSqFt is { } sizeMin)
            query = query.Where(p => p.SizeSqFt >= sizeMin);
        if (f.SizeMaxSqFt is { } sizeMax)
            query = query.Where(p => p.SizeSqFt <= sizeMax);
        if (f.Bhk is { Count: > 0 } bhk)
        {
            var list = bhk.Select(b => (int?)b).ToList();
            query = query.Where(p => list.Contains(p.Bhk));
        }
        if (f.Facings is { Count: > 0 } facings)
        {
            var list = facings.Select(x => (Facing?)x).ToList();
            query = query.Where(p => list.Contains(p.Facing));
        }
        if (f.Corner is { } corner)
            query = query.Where(p => p.Corner == corner);
        if (f.ParkFacing is { } park)
            query = query.Where(p => p.ParkFacing == park);
        if (f.Furnishings is { Count: > 0 } furnishings)
        {
            var list = furnishings.Select(x => (Furnishing?)x).ToList();
            query = query.Where(p => list.Contains(p.Furnishing));
        }
        if (f.Stale is { } stale)
        {
            var s = await EstateSettings.LoadAsync(settings, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var saleCut = now.AddDays(-s.SaleFreshDays);
            var rentCut = now.AddDays(-s.RentFreshDays);
            query = stale
                ? query.Where(p => (p.Purpose == PropertyPurpose.Sale && p.LastConfirmedAt < saleCut) || (p.Purpose == PropertyPurpose.Rent && p.LastConfirmedAt < rentCut))
                : query.Where(p => (p.Purpose == PropertyPurpose.Sale && p.LastConfirmedAt >= saleCut) || (p.Purpose == PropertyPurpose.Rent && p.LastConfirmedAt >= rentCut));
        }
        foreach (var word in (f.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
            query = query.Where(p => p.SearchText.Contains(word));
        foreach (var c in f.Custom ?? [])
        {
            var key = c.Key;
            var value = c.Value;
            query = query.Where(p => p.Custom[key] == value);
        }

        var total = await query.CountAsync(ct);
        query = f.Sort switch
        {
            PropertySort.PriceLow => query.OrderBy(p => p.PriceValue),
            PropertySort.PriceHigh => query.OrderByDescending(p => p.PriceValue),
            PropertySort.SizeLarge => query.OrderByDescending(p => p.SizeSqFt),
            PropertySort.LastConfirmed => query.OrderBy(p => p.LastConfirmedAt),
            _ => query.OrderByDescending(p => p.Id),
        };
        var rows = await query.Skip(Math.Max(0, f.Skip)).Take(Math.Clamp(f.Take, 1, 200)).ToListAsync(ct);
        return new PropertyPageDto(total, await mapper.ManyAsync(rows, ct));
    }
}

public sealed record GetPropertyQuery(string PropertyId) : IRequest<PropertyDto>;

public sealed class GetPropertyHandler(IPropertyRepository properties, PropertyMapper mapper) : IRequestHandler<GetPropertyQuery, PropertyDto>
{
    public async Task<PropertyDto> Handle(GetPropertyQuery request, CancellationToken ct) =>
        await mapper.OneAsync(await PropertyLookup.RequireAsync(properties, request.PropertyId, ct), ct);
}

/// <summary>Properties to check (D6): available or on hold, past the dealer's freshness days, oldest first.</summary>
public sealed record GetStalePropertiesQuery(int Take = 50) : IRequest<IReadOnlyList<PropertyDto>>;

public sealed class GetStalePropertiesHandler(IPropertyRepository properties, IEstateSettingsRepository settings, PropertyMapper mapper, TimeProvider clock)
    : IRequestHandler<GetStalePropertiesQuery, IReadOnlyList<PropertyDto>>
{
    public async Task<IReadOnlyList<PropertyDto>> Handle(GetStalePropertiesQuery request, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var saleCut = now.AddDays(-s.SaleFreshDays);
        var rentCut = now.AddDays(-s.RentFreshDays);
        var rows = await properties.GetDocuments(p => (p.Status == PropertyStatus.Available || p.Status == PropertyStatus.OnHold)
                && ((p.Purpose == PropertyPurpose.Sale && p.LastConfirmedAt < saleCut) || (p.Purpose == PropertyPurpose.Rent && p.LastConfirmedAt < rentCut)))
            .OrderBy(p => p.LastConfirmedAt).Take(Math.Clamp(request.Take, 1, 200)).ToListAsync(ct);
        return await mapper.ManyAsync(rows, ct);
    }
}

/// <summary>Properties that look like the one being entered (design 4.6): the same society and unit, or the same owner phone, type and size.</summary>
public sealed record GetDuplicatesQuery(PropertyInput Input, string? ExcludeId, IReadOnlyList<string>? OwnerPhones) : IRequest<IReadOnlyList<DuplicateDto>>;

public sealed class GetDuplicatesHandler(IPropertyRepository properties, IEstateSettingsRepository settings)
    : IRequestHandler<GetDuplicatesQuery, IReadOnlyList<DuplicateDto>>
{
    public async Task<IReadOnlyList<DuplicateDto>> Handle(GetDuplicatesQuery request, CancellationToken ct)
    {
        var input = request.Input;
        var found = new Dictionary<string, DuplicateDto>();
        var society = EstateText.Clean(input.Society);
        var unit = EstateText.Clean(input.UnitNumber);
        var locality = EstateText.Clean(input.Locality) is { } l ? EstateText.Key(l) : null;
        if (unit is not null && locality is not null)
        {
            var same = await properties.GetDocuments(p => p.LocalityKey == locality && p.UnitNumber == unit && p.Society == society && p.Id != request.ExcludeId)
                .Take(5).ToListAsync(ct);
            foreach (var p in same)
                found[p.Id] = new DuplicateDto(p.Id, p.Code, p.Title, "Same locality, society and unit number");
        }

        var phones = (request.OwnerPhones ?? []).Select(EstateText.Phone).OfType<string>().ToList();
        if (phones.Count > 0)
        {
            var s = await EstateSettings.LoadAsync(settings, ct);
            var size = (AreaMath.Model(input.SuperArea, s) ?? AreaMath.Model(input.BuiltUpArea, s) ?? AreaMath.Model(input.CarpetArea, s)
                ?? AreaMath.Model(input.PlotArea, s))?.SqFt;
            var sameOwner = await properties.GetDocuments(p => p.Type == input.Type && p.OwnerPhones.Any(x => phones.Contains(x)) && p.Id != request.ExcludeId)
                .Take(20).ToListAsync(ct);
            foreach (var p in sameOwner.Where(p => size is null || p.SizeSqFt is null || Math.Abs(p.SizeSqFt.Value - size.Value) <= size.Value * 0.05m))
                found.TryAdd(p.Id, new DuplicateDto(p.Id, p.Code, p.Title, "Same owner, type and size"));
        }
        return [.. found.Values];
    }
}

// ── Write ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Adds (no id) or changes a property; the matches follow at once (D10).</summary>
public sealed record SavePropertyCommand(string? PropertyId, PropertyInput Input) : IRequest<PropertyDto>;

public sealed class SavePropertyValidator : AbstractValidator<SavePropertyCommand>
{
    public SavePropertyValidator()
    {
        RuleFor(x => x.Input.Locality).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Input.Society).MaximumLength(160);
        RuleFor(x => x.Input.Notes).MaximumLength(4000);
        RuleFor(x => x.Input.Bhk).InclusiveBetween(0, 20).When(x => x.Input.Bhk is not null);
        RuleFor(x => x.Input.Floor).InclusiveBetween(-3, 200).When(x => x.Input.Floor is not null);
        RuleFor(x => x.Input.Price).LessThanOrEqualTo(100_000_000_000m).When(x => x.Input.Price is not null);
    }
}

public sealed class SavePropertyHandler(
    IPropertyRepository properties, IEstateSettingsRepository settings, ICustomFieldRepository fields, ILocalityRepository localities,
    ICounterRepository counters, MatchEngine engine, PropertyMapper mapper, TimeProvider clock)
    : IRequestHandler<SavePropertyCommand, PropertyDto>
{
    public const string CodeCounter = "property";

    public async Task<PropertyDto> Handle(SavePropertyCommand request, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct);
        var isNew = string.IsNullOrEmpty(request.PropertyId);
        var property = isNew
            ? new PropertyDocument
            {
                Id = properties.GetNewId,
                Code = $"P-{await counters.NextAsync(CodeCounter, ct):0000}",
                Locality = string.Empty,
                LocalityKey = string.Empty,
                LastConfirmedAt = clock.GetUtcNow().UtcDateTime,
                ConfirmedVia = ConfirmedVia.Added,
            }
            : await PropertyLookup.RequireAsync(properties, request.PropertyId!, ct);
        var oldPrice = property.PriceValue;

        await PropertyWriter.ApplyAsync(property, request.Input, s, fields, ct);
        if (isNew)
            await properties.AddAsync(property, ct);
        else
            await properties.UpdateAsync(property, ct);
        await localities.EnsureAsync(property.Locality, property.LocalityKey, property.City, ct);

        await engine.ForPropertyAsync(property, ct);
        return await mapper.OneAsync(property, ct);
    }
}

/// <summary>
/// "Still available?" answered (D6): the status now, perhaps a new price, and how it was confirmed. Confirming makes the
/// property fresh; back to available it is matched again; out of the stock its new matches are dropped.
/// </summary>
public sealed record ConfirmPropertyCommand(string PropertyId, ConfirmPropertyInput Input) : IRequest<PropertyDto>;

public sealed class ConfirmPropertyHandler(IPropertyRepository properties, MatchEngine engine, PropertyMapper mapper, TimeProvider clock)
    : IRequestHandler<ConfirmPropertyCommand, PropertyDto>
{
    public async Task<PropertyDto> Handle(ConfirmPropertyCommand request, CancellationToken ct)
    {
        var property = await PropertyLookup.RequireAsync(properties, request.PropertyId, ct);
        var input = request.Input;
        property.Status = input.Status;
        property.HoldUntil = input.Status == PropertyStatus.OnHold
            ? EstateText.Date(input.HoldUntil, "Hold until") ?? throw new ValidatorException("Say until when it is on hold.")
            : null;
        if (input.Price is { } price)
        {
            if (price <= 0)
                throw new ValidatorException("The price must be more than zero.");
            if (property.Purpose == PropertyPurpose.Rent)
                property.Rent = price;
            else
                property.Price = price;
            property.PriceValue = price;
        }
        property.LastConfirmedAt = clock.GetUtcNow().UtcDateTime;
        property.ConfirmedVia = input.Via == ConfirmedVia.Added ? ConfirmedVia.Call : input.Via;
        await properties.UpdateAsync(property, ct);

        if (property.Status == PropertyStatus.Available)
            await engine.ForPropertyAsync(property, ct);
        else
            await engine.DropForPropertyAsync(property.Id, ct);
        return await mapper.OneAsync(property, ct);
    }
}

public sealed record DeletePropertyCommand(string PropertyId) : IRequest<bool>;

public sealed class DeletePropertyHandler(IPropertyRepository properties, MatchEngine engine) : IRequestHandler<DeletePropertyCommand, bool>
{
    public async Task<bool> Handle(DeletePropertyCommand request, CancellationToken ct)
    {
        var property = await PropertyLookup.RequireAsync(properties, request.PropertyId, ct);
        await properties.DeleteAsync(property.Id, ct);
        await engine.DropForPropertyAsync(property.Id, ct);
        return true;
    }
}

/// <summary>
/// The property's current owners (D7): one or more parties, each with a share and how they hold it. No shares given =
/// equal shares; shares given must all be given and total 100%.
/// </summary>
public sealed record SetPropertyOwnersCommand(string PropertyId, IReadOnlyList<PropertyOwnerInput> Owners) : IRequest<PropertyDto>;

public sealed class SetPropertyOwnersHandler(IPropertyRepository properties, IPartyRepository parties, PropertyMapper mapper, Parties.PartyContacts contacts)
    : IRequestHandler<SetPropertyOwnersCommand, PropertyDto>
{
    public async Task<PropertyDto> Handle(SetPropertyOwnersCommand request, CancellationToken ct)
    {
        var property = await PropertyLookup.RequireAsync(properties, request.PropertyId, ct);
        var owners = request.Owners.GroupBy(o => o.PartyId).Select(g => g.First()).ToList();
        var ids = owners.Select(o => o.PartyId).ToList();
        var found = (await parties.GetDocuments(p => ids.Contains(p.Id)).ToListAsync(ct)).ToDictionary(p => p.Id);
        if (found.Count != ids.Count)
            throw new ValidatorException("An owner was not found. Add the person first.");

        property.Owners = [.. owners.Zip(Shares(owners), (o, share) => new OwnerModel { PartyId = o.PartyId, SharePercent = share, Holding = o.Holding })];
        property.OwnerPhones = [.. owners.Select(o => found[o.PartyId].Phone).OfType<string>().Distinct()];
        await properties.UpdateAsync(property, ct);
        foreach (var owner in found.Values)
            await contacts.SyncAsync(owner, ct, Parties.PartyContacts.Owner);
        return await mapper.OneAsync(property, ct);
    }

    /// <summary>Equal shares (the remainder to the first), or the given ones when they total 100%.</summary>
    public static IReadOnlyList<decimal> Shares(IReadOnlyList<PropertyOwnerInput> owners)
    {
        if (owners.Count == 0)
            return [];
        if (owners.All(o => o.SharePercent is null))
        {
            var each = Math.Floor(10000m / owners.Count) / 100m;
            var first = 100m - each * (owners.Count - 1);
            return [first, .. Enumerable.Repeat(each, owners.Count - 1)];
        }
        if (owners.Any(o => o.SharePercent is null or <= 0))
            throw new ValidatorException("Give every owner a share, or none for equal shares.");
        var total = owners.Sum(o => o.SharePercent!.Value);
        if (Math.Abs(total - 100m) > 0.01m)
            throw new ValidatorException($"The shares add up to {total:0.##}%, not 100%.");
        return [.. owners.Select(o => o.SharePercent!.Value)];
    }
}

internal static class PropertyLookup
{
    public static async Task<PropertyDocument> RequireAsync(IPropertyRepository properties, string id, CancellationToken ct) =>
        await properties.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
}
