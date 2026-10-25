using System.Globalization;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;

namespace ZeroNovaRealEstate.Application.Features.Properties;

/// <summary>
/// Properties to what members see, in one batch: owners' names (phones only with POWV), the dealer's field labels,
/// stale or fresh, and the open matches the caller may see.
/// </summary>
public sealed class PropertyMapper(
    IPartyRepository parties, ICustomFieldRepository fields, IMatchRepository matches, IRequirementRepository requirements,
    IEstateSettingsRepository settings, EstateScope scope, TimeProvider clock)
{
    public async Task<PropertyDto> OneAsync(PropertyDocument property, CancellationToken ct) => (await ManyAsync([property], ct))[0];

    public async Task<IReadOnlyList<PropertyDto>> ManyAsync(IReadOnlyList<PropertyDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var s = await EstateSettings.LoadAsync(settings, ct);
        var labels = (await fields.GetDocuments().ToListAsync(ct)).ToDictionary(f => f.Key, f => f.Label);
        var partyIds = rows.SelectMany(p => p.Owners.Select(o => o.PartyId)).Distinct().ToList();
        var owners = partyIds.Count == 0
            ? new Dictionary<string, PartyDocument>()
            : (await parties.GetDocuments(p => partyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);

        var ids = rows.Select(p => p.Id).ToList();
        var open = matches.GetDocuments(m => ids.Contains(m.PropertyId) && m.Status == MatchStatus.New);
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            open = open.Where(m => m.AgentId == me);
        }
        var counts = (await open.Select(m => m.PropertyId).ToListAsync(ct)).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        var now = clock.GetUtcNow().UtcDateTime;
        return [.. rows.Select(p => Map(p, s, labels, owners, counts.GetValueOrDefault(p.Id), scope.SeesOwners, now))];
    }

    private static PropertyDto Map(PropertyDocument p, EstateSettingsDocument s, IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, PartyDocument> owners, int openMatches, bool seesOwners, DateTime now) => new(
        p.Id, p.Code, p.Title, p.Purpose, p.Type, p.Status, p.HoldUntil, EstateSettings.IsStale(p, s, now),
        new DateTimeOffset(DateTime.SpecifyKind(p.LastConfirmedAt, DateTimeKind.Utc)), p.ConfirmedVia,
        p.City, p.Locality, p.Society, p.Block, seesOwners ? p.UnitNumber : null, p.Latitude, p.Longitude,
        AreaMath.Dto(p.PlotArea), AreaMath.Dto(p.BuiltUpArea), AreaMath.Dto(p.CarpetArea), AreaMath.Dto(p.SuperArea), p.SizeSqFt,
        p.FrontFeet, p.DepthFeet, p.Price, p.Negotiable, seesOwners ? p.OwnerExpectation : null,
        p.Price is { } price && p.SizeSqFt is { } size && size > 0 ? Math.Round(price / size, 0) : null,
        p.Rent, p.Deposit, p.Maintenance, p.LockInMonths, p.NoticeMonths,
        p.Bhk, p.Bathrooms, p.Balconies, p.Floor, p.TotalFloors, p.Lift, p.CoveredParking, p.OpenParking, p.Furnishing, p.AgeYears,
        p.Construction, p.PossessionDate, p.ServantRoom, p.PoojaRoom, p.StoreRoom, p.StudyRoom, p.PowerBackup, p.Gated, p.Amenities,
        p.Facing, p.Corner, p.ParkFacing, p.RoadWidthFeet, p.Tenure, p.KhasraNumber,
        p.Washrooms, p.PowerLoadKw, p.Pantry, p.CurrentlyLeased, p.LeasedRent,
        p.AvailableFrom, p.TenantPreferences, p.PetsAllowed,
        p.Registry, p.Mutation, p.LoanApproved, p.Noc, p.OccupancyCertificate, p.PropertyTaxPaid,
        p.Source, p.SourceDealerName, seesOwners ? p.SourceDealerPhone : null, p.KeysWith, p.VisitTimings, p.Mandate, p.MandateUntil,
        p.Occupancy, [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl, p.Notes,
        [.. p.Custom.Select(kv => new CustomValueDto(kv.Key, labels.GetValueOrDefault(kv.Key) ?? kv.Key, kv.Value))],
        [.. p.Owners.Select(o => new PropertyOwnerDto(o.PartyId, owners.GetValueOrDefault(o.PartyId)?.Name ?? "—",
            seesOwners ? owners.GetValueOrDefault(o.PartyId)?.Phone : null, o.SharePercent, o.Holding))],
        [.. p.History.Select(h => new Models.Deals.OwnershipRecordDto(h.FromNames, h.ToNames, h.Kind, h.Date, h.Note, h.DealId))],
        openMatches, p.CreatedBy?.ChangedDate);
}

/// <summary>A property input onto its document: cleaned, the other types' fields left out, sizes in sq ft, the title made.</summary>
public static class PropertyWriter
{
    public static async Task ApplyAsync(PropertyDocument p, PropertyInput input, EstateSettingsDocument s, ICustomFieldRepository fields, CancellationToken ct)
    {
        var built = Matching.MatchEngine.IsBuilt(input.Type);
        var commercial = input.Type is PropertyType.Shop or PropertyType.Office or PropertyType.Showroom or PropertyType.Warehouse;
        var rent = input.Purpose == PropertyPurpose.Rent;

        p.Purpose = input.Purpose;
        p.Type = input.Type;
        p.City = EstateText.Clean(input.City) ?? s.DefaultCity;
        p.Locality = EstateText.Clean(input.Locality) ?? throw new ValidatorException("Enter the locality (sector, colony or area).");
        p.LocalityKey = EstateText.Key(p.Locality);
        p.Society = EstateText.Clean(input.Society);
        p.Block = EstateText.Clean(input.Block);
        p.UnitNumber = EstateText.Clean(input.UnitNumber);
        p.Latitude = input.Latitude;
        p.Longitude = input.Longitude;

        p.PlotArea = AreaMath.Model(input.PlotArea, s);
        p.BuiltUpArea = built ? AreaMath.Model(input.BuiltUpArea, s) : null;
        p.CarpetArea = built ? AreaMath.Model(input.CarpetArea, s) : null;
        p.SuperArea = built ? AreaMath.Model(input.SuperArea, s) : null;
        p.SizeSqFt = (p.SuperArea ?? p.BuiltUpArea ?? p.CarpetArea ?? p.PlotArea)?.SqFt;
        p.FrontFeet = Positive(input.FrontFeet);
        p.DepthFeet = Positive(input.DepthFeet);

        p.Price = rent ? null : Positive(input.Price);
        p.Negotiable = input.Negotiable;
        p.OwnerExpectation = rent ? null : Positive(input.OwnerExpectation);
        p.Rent = rent ? Positive(input.Rent) : null;
        p.Deposit = rent ? Positive(input.Deposit) : null;
        p.Maintenance = Positive(input.Maintenance);
        p.LockInMonths = rent ? input.LockInMonths : null;
        p.NoticeMonths = rent ? input.NoticeMonths : null;
        p.PriceValue = rent ? p.Rent : p.Price;

        p.Bhk = built && !commercial ? input.Bhk : null;
        p.Bathrooms = built ? input.Bathrooms : null;
        p.Balconies = built && !commercial ? input.Balconies : null;
        p.Floor = built ? input.Floor : null;
        p.TotalFloors = built ? input.TotalFloors : null;
        p.Lift = built ? input.Lift : null;
        p.CoveredParking = input.CoveredParking;
        p.OpenParking = input.OpenParking;
        p.Furnishing = built ? input.Furnishing : null;
        p.AgeYears = built ? input.AgeYears : null;
        p.Construction = built ? input.Construction : null;
        p.PossessionDate = EstateText.Date(input.PossessionDate, "Possession date");
        p.ServantRoom = built && input.ServantRoom;
        p.PoojaRoom = built && input.PoojaRoom;
        p.StoreRoom = built && input.StoreRoom;
        p.StudyRoom = built && input.StudyRoom;
        p.PowerBackup = input.PowerBackup;
        p.Gated = input.Gated;
        p.Amenities = [.. (input.Amenities ?? []).Select(EstateText.Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

        p.Facing = input.Facing;
        p.Corner = input.Corner;
        p.ParkFacing = input.ParkFacing;
        p.RoadWidthFeet = Positive(input.RoadWidthFeet);
        p.Tenure = input.Tenure;
        p.KhasraNumber = EstateText.Clean(input.KhasraNumber);

        p.Washrooms = commercial ? input.Washrooms : null;
        p.PowerLoadKw = commercial ? Positive(input.PowerLoadKw) : null;
        p.Pantry = commercial ? input.Pantry : null;
        p.CurrentlyLeased = commercial ? input.CurrentlyLeased : null;
        p.LeasedRent = commercial ? Positive(input.LeasedRent) : null;

        p.AvailableFrom = rent ? EstateText.Date(input.AvailableFrom, "Available from") : null;
        p.TenantPreferences = rent ? [.. (input.TenantPreferences ?? []).Distinct()] : [];
        p.PetsAllowed = rent ? input.PetsAllowed : null;

        p.Registry = input.Registry;
        p.Mutation = input.Mutation;
        p.LoanApproved = input.LoanApproved;
        p.Noc = input.Noc;
        p.OccupancyCertificate = input.OccupancyCertificate;
        p.PropertyTaxPaid = input.PropertyTaxPaid;

        p.Source = input.Source;
        p.SourceDealerName = input.Source == PropertySource.OtherDealer ? EstateText.Clean(input.SourceDealerName) : null;
        p.SourceDealerPhone = input.Source == PropertySource.OtherDealer ? EstateText.Phone(input.SourceDealerPhone) : null;
        p.KeysWith = input.KeysWith;
        p.VisitTimings = EstateText.Clean(input.VisitTimings);
        p.Mandate = input.Mandate;
        p.MandateUntil = input.Mandate == MandateKind.Exclusive ? EstateText.Date(input.MandateUntil, "Mandate until") : null;
        p.Occupancy = input.Occupancy;

        p.Photos = [.. (input.Photos ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Url)).Take(40)
            .Select(x => new PhotoModel { Key = x.Key.Trim(), Url = x.Url.Trim() })];
        p.VideoUrl = EstateText.Clean(input.VideoUrl);
        p.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;
        p.Custom = await CustomValuesAsync(input, fields, ct);

        p.Title = Title(p);
        p.SearchText = string.Join(' ', new[] { p.Title, p.Locality, p.Society, p.Block, p.City, p.Notes, p.Code }
            .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
    }

    /// <summary>"3 BHK Builder Floor in Sector 70", "200 sq yd Plot in Sector 70", "Shop in Phase 7".</summary>
    public static string Title(PropertyDocument p)
    {
        var type = p.Type switch
        {
            PropertyType.BuilderFloor => "Builder Floor",
            PropertyType.AgriculturalLand => "Agricultural Land",
            PropertyType.IndustrialPlot => "Industrial Plot",
            PropertyType.Pg => "PG",
            _ => p.Type.ToString(),
        };
        var lead = p.Bhk is { } bhk ? $"{bhk} BHK " : p.PlotArea is { } plot && !Matching.MatchEngine.IsBuilt(p.Type) ? $"{AreaMath.Label(plot)} " : string.Empty;
        var corner = p.Corner && !Matching.MatchEngine.IsBuilt(p.Type) ? "Corner " : string.Empty;
        return $"{lead}{corner}{type} in {p.Locality}";
    }

    private static decimal? Positive(decimal? value) => value is { } v ? (v < 0 ? throw new ValidatorException("Amounts and sizes cannot be negative.") : v) : null;

    /// <summary>The dealer's own fields: unknown keys dropped, a choice must be one of its options, a number must be a number.</summary>
    private static async Task<Dictionary<string, string>> CustomValuesAsync(PropertyInput input, ICustomFieldRepository fields, CancellationToken ct)
    {
        if (input.Custom is not { Count: > 0 })
            return [];
        var defined = (await fields.GetDocuments().ToListAsync(ct)).ToDictionary(f => f.Key);
        var values = new Dictionary<string, string>();
        foreach (var (key, raw) in input.Custom.Select(c => (c.Key, c.Value?.Trim() ?? string.Empty)))
        {
            if (raw.Length == 0 || !defined.TryGetValue(key, out var field))
                continue;
            if (field.PropertyTypes.Count > 0 && !field.PropertyTypes.Contains(input.Type))
                continue;
            values[key] = field.Kind switch
            {
                CustomFieldKind.Choice => field.Options.FirstOrDefault(o => string.Equals(o, raw, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ValidatorException($"{field.Label}: choose one of {string.Join(", ", field.Options)}."),
                CustomFieldKind.Number => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : throw new ValidatorException($"{field.Label} must be a number."),
                CustomFieldKind.YesNo => raw.ToLowerInvariant() is "yes" or "true" or "1" ? "yes" : "no",
                _ => raw.Length > 200 ? raw[..200] : raw,
            };
        }
        return values;
    }
}
