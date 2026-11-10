using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Website;

namespace ZeroNovaRealEstate.Application.Features.Website;

// ── Settings (PSEM) ───────────────────────────────────────────────────────────────────────────────────

/// <summary>The company's website: on or off and its code — the code made on first ask (as Tutor's coaching code).</summary>
public sealed record GetEstateWebsiteSettingsQuery : IRequest<EstateWebsiteSettingsDto>;

public sealed class GetEstateWebsiteSettingsHandler(IWebsiteRepository websites, IPropertyRepository properties, IProjectRepository projects,
    IEstateSettingsRepository settings, TimeProvider clock)
    : IRequestHandler<GetEstateWebsiteSettingsQuery, EstateWebsiteSettingsDto>
{
    public async Task<EstateWebsiteSettingsDto> Handle(GetEstateWebsiteSettingsQuery request, CancellationToken ct) =>
        await WebsiteCodes.DtoAsync(await WebsiteCodes.EnsureAsync(websites, ct), properties, projects,
            await WebsiteFreshness.LoadAsync(settings, null, clock, ct), ct);
}

/// <summary>Turns the website on or off, or gives it another code (unique across companies). Null keeps.</summary>
public sealed record SaveEstateWebsiteSettingsCommand(EstateWebsiteSettingsInput Input) : IRequest<EstateWebsiteSettingsDto>;

public sealed class SaveEstateWebsiteSettingsValidator : AbstractValidator<SaveEstateWebsiteSettingsCommand>
{
    public SaveEstateWebsiteSettingsValidator() =>
        RuleFor(x => x.Input.Code).Must(c => WebsiteCodes.Normalize(c) is not null)
            .When(x => !string.IsNullOrWhiteSpace(x.Input.Code))
            .WithMessage("A website code has 4 to 20 letters and digits.");
}

public sealed class SaveEstateWebsiteSettingsHandler(IWebsiteRepository websites, IPropertyRepository properties, IProjectRepository projects,
    IEstateSettingsRepository settings, TimeProvider clock)
    : IRequestHandler<SaveEstateWebsiteSettingsCommand, EstateWebsiteSettingsDto>
{
    public async Task<EstateWebsiteSettingsDto> Handle(SaveEstateWebsiteSettingsCommand request, CancellationToken ct)
    {
        var site = await WebsiteCodes.EnsureAsync(websites, ct);
        if (request.Input.Enabled is { } enabled)
            site.Enabled = enabled;
        if (WebsiteCodes.Normalize(request.Input.Code) is { } code && code != site.Code)
        {
            if (await websites.FindByCodeAnyTenantAsync(code, ct) is not null)
                throw new ValidatorException($"The website code {code} is taken; choose another.");
            site.Code = code;
        }
        try
        {
            await websites.UpdateAsync(site, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ValidatorException($"The website code {site.Code} is taken; choose another.");
        }
        return await WebsiteCodes.DtoAsync(site, properties, projects, await WebsiteFreshness.LoadAsync(settings, null, clock, ct), ct);
    }
}

/// <summary>The website's code: made once per company, unique across companies, letters and digits in upper case.</summary>
public static class WebsiteCodes
{
    /// <summary>Six letters and digits without look-alikes (0/O, 1/I), as Tutor's coaching code.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>The code in upper case, or null when it is not 4 to 20 letters and digits.</summary>
    public static string? Normalize(string? code) =>
        code?.Trim().ToUpperInvariant() is { Length: >= 4 and <= 20 } c && c.All(char.IsAsciiLetterOrDigit) ? c : null;

    public static async Task<EstateWebsiteDocument> EnsureAsync(IWebsiteRepository websites, CancellationToken ct)
    {
        if (await websites.GetDocuments().FirstOrDefaultAsync(ct) is { } existing)
            return existing;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = new string(Enumerable.Range(0, 6).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            var site = new EstateWebsiteDocument { Id = websites.GetNewId, Code = code };
            try
            {
                await websites.AddAsync(site, ct);
                return site;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Taken by another company, or this company's website was made by a parallel call.
                if (await websites.GetDocuments().FirstOrDefaultAsync(ct) is { } raced)
                    return raced;
            }
        }
        throw new InvalidOperationException("Could not make a unique website code.");
    }

    internal static async Task<EstateWebsiteSettingsDto> DtoAsync(EstateWebsiteDocument site, IPropertyRepository properties, IProjectRepository projects,
        WebsiteFreshness fresh, CancellationToken ct) =>
        new(site.Enabled, site.Code, await WebsiteListings.Properties(properties, null, fresh).CountAsync(ct),
            await WebsiteListings.Projects(projects, null).CountAsync(ct));
}

/// <summary>Slugs and the website's search words.</summary>
public static class WebsiteSlugs
{
    /// <summary>Lower case letters and digits (any script) joined by single dashes, cut at <paramref name="max"/>.</summary>
    public static string Slugify(string? text, int max = 90)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > max ? slug[..max].TrimEnd('-') : slug;
    }

    public static string Text(params string?[] parts) =>
        string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

    /// <summary>"3-bhk-flat-for-sale-sector-70-mohali-p0001": the title without its locality, sale or rent, locality, city, code.</summary>
    public static string ForProperty(PropertyDocument p)
    {
        var suffix = $" in {p.Locality}";
        var head = p.Title.EndsWith(suffix, StringComparison.Ordinal) ? p.Title[..^suffix.Length] : p.Title;
        var city = p.City is { } c && !p.Locality.Contains(c, StringComparison.OrdinalIgnoreCase) ? c : null;
        var words = Slugify($"{head} for {(p.Purpose == PropertyPurpose.Rent ? "rent" : "sale")} {p.Locality} {city}", 80);
        return $"{words}-{Slugify(new string(p.Code.Where(char.IsLetterOrDigit).ToArray()))}";
    }

    /// <summary>A property's slug, unique within the company (a number added in the unlikely case it is taken).</summary>
    public static Task<string> ForPropertyAsync(IPropertyRepository properties, PropertyDocument p, CancellationToken ct) =>
        UniqueAsync(ForProperty(p), s => properties.GetDocuments(x => x.Slug == s && x.Id != p.Id).AnyAsync(ct));

    /// <summary>"aetro-sapphire"; "aetro-sapphire-pj0002" when another project has the name.</summary>
    public static async Task<string> ForProjectAsync(IProjectRepository projects, ProjectDocument p, CancellationToken ct)
    {
        var code = Slugify(new string(p.Code.Where(char.IsLetterOrDigit).ToArray()));
        var name = Slugify(p.Name, 80);
        Task<bool> Taken(string s) => projects.GetDocuments(x => x.Slug == s && x.Id != p.Id).AnyAsync(ct);
        if (name.Length > 0 && !await Taken(name))
            return name;
        return await UniqueAsync(name.Length > 0 ? $"{name}-{code}" : code, Taken);
    }

    private static async Task<string> UniqueAsync(string slug, Func<string, Task<bool>> taken)
    {
        if (!await taken(slug))
            return slug;
        for (var n = 2; ; n++)
            if (!await taken($"{slug}-{n}"))
                return $"{slug}-{n}";
    }
}

/// <summary>
/// The dealer's freshness limits as instants (D6): a property for sale last confirmed before <see cref="SaleCut"/> (for
/// rent, <see cref="RentCut"/>) is stale.
/// </summary>
public sealed record WebsiteFreshness(DateTime SaleCut, DateTime RentCut)
{
    public static async Task<WebsiteFreshness> LoadAsync(IEstateSettingsRepository settings, string? company, TimeProvider clock, CancellationToken ct)
    {
        var s = await EstateSettings.LoadAsync(settings, ct, company);
        var now = clock.GetUtcNow().UtcDateTime;
        return new WebsiteFreshness(now.AddDays(-s.SaleFreshDays), now.AddDays(-s.RentFreshDays));
    }

    public bool IsFresh(PropertyDocument p) => p.LastConfirmedAt >= (p.Purpose == PropertyPurpose.Rent ? RentCut : SaleCut);
}

/// <summary>
/// What the website lists (cms doc 06 §12): properties with Show on website that are available or on hold and not stale
/// (D6: a stale listing is never sent to a customer; owner decision 2026-11-10) — never a project unit on its own — and
/// projects with Show on website; an own project only with its RERA registration or marked as needing none (RERA s.3, as
/// for sharing). <paramref name="company"/> null = the caller's company.
/// </summary>
public static class WebsiteListings
{
    public static IQueryable<PropertyDocument> Properties(IPropertyRepository properties, string? company, WebsiteFreshness fresh)
    {
        var saleCut = fresh.SaleCut;
        var rentCut = fresh.RentCut;
        return properties.GetDocuments(p => p.ShowOnWebsite && p.ProjectId == null && (p.Status == PropertyStatus.Available || p.Status == PropertyStatus.OnHold)
            && ((p.Purpose == PropertyPurpose.Rent && p.LastConfirmedAt >= rentCut) || (p.Purpose != PropertyPurpose.Rent && p.LastConfirmedAt >= saleCut)),
            false, company);
    }

    public static IQueryable<ProjectDocument> Projects(IProjectRepository projects, string? company) =>
        projects.GetDocuments(p => p.ShowOnWebsite && (p.Kind == ProjectKind.Partner || p.ReraNotRequired || p.Reras.Any()), false, company);

    public static bool MayList(ProjectDocument p) => p.Kind == ProjectKind.Partner || p.ReraNotRequired || p.Reras.Count > 0;

    public static bool IsListed(PropertyDocument p, WebsiteFreshness fresh) =>
        !p.IsDeleted && p.ShowOnWebsite && p.ProjectId is null && p.Status is PropertyStatus.Available or PropertyStatus.OnHold && fresh.IsFresh(p);

    public static readonly PropertyType[] CommercialTypes = [PropertyType.Shop, PropertyType.Office, PropertyType.Showroom, PropertyType.Warehouse];
    public static readonly PropertyType[] PlotTypes = [PropertyType.Plot, PropertyType.IndustrialPlot, PropertyType.AgriculturalLand];

    /// <summary>
    /// Sold out with none available; few left with at most max(3, 10% of the type's units) available; available otherwise.
    /// Withdrawn units do not count. A type whose units are not made yet is available (open for enquiries).
    /// </summary>
    public static EstateWebsiteAvailability Availability(IReadOnlyCollection<PropertyStatus> units)
    {
        if (units.Count == 0)
            return EstateWebsiteAvailability.Available;
        var total = units.Count(s => s != PropertyStatus.Withdrawn);
        var available = units.Count(s => s == PropertyStatus.Available);
        if (available == 0)
            return EstateWebsiteAvailability.SoldOut;
        return available <= Math.Max(3, total / 10) ? EstateWebsiteAvailability.FewLeft : EstateWebsiteAvailability.Available;
    }

    public static EstateWebsitePropertyDto Map(PropertyDocument p) => new(
        p.Id, p.Slug ?? string.Empty, p.Code, p.Title, p.Purpose, p.Type, p.Status, p.Featured, p.City, p.Locality, p.Society,
        AreaMath.Dto(p.PlotArea), AreaMath.Dto(p.BuiltUpArea), AreaMath.Dto(p.CarpetArea), AreaMath.Dto(p.SuperArea), p.SizeSqFt, p.FrontFeet, p.DepthFeet,
        p.Price, p.PriceValue is null, p.Negotiable, p.Rent, p.Deposit, p.Maintenance,
        p.Bhk, p.Bathrooms, p.Balconies, p.Floor, p.TotalFloors, p.Lift, p.CoveredParking, p.OpenParking, p.Furnishing, p.AgeYears, p.Construction,
        p.PossessionDate, p.ServantRoom, p.PoojaRoom, p.StoreRoom, p.StudyRoom, p.PowerBackup, p.Gated, p.Amenities,
        p.Facing, p.Corner, p.ParkFacing, p.RoadWidthFeet, p.Tenure, p.Washrooms, p.Pantry,
        p.AvailableFrom, p.TenantPreferences, p.PetsAllowed, [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl, p.Description);
}

/// <summary>Listed properties with their ready videos (D36): one query for the page.</summary>
internal static class WebsiteVideos
{
    public static async Task<IReadOnlyList<EstateWebsitePropertyDto>> MapAsync(IReadOnlyList<PropertyDocument> rows, Videos.EstateVideoBook videos, string company,
        CancellationToken ct)
    {
        var ready = await videos.OfAsync(Models.Videos.EstateListingKind.Property, [.. rows.Select(p => p.Id)], true, company, ct);
        return [.. rows.Select(p => WebsiteListings.Map(p) with { Videos = [.. ready[p.Id].Select(Videos.EstateVideoBook.Public)] })];
    }
}

/// <summary>The website with this code, when it is on — else "not available", the same for a wrong code and a website turned off.</summary>
internal static class WebsiteLookup
{
    public static async Task<EstateWebsiteDocument> RequireAsync(IWebsiteRepository websites, string code, CancellationToken ct) =>
        WebsiteCodes.Normalize(code) is { } c && await websites.FindByCodeAnyTenantAsync(c, ct) is { Enabled: true } site
            ? site
            : throw new ValidatorException("This website is not available.");
}

// ── The public listings (anonymous, by website code) ──────────────────────────────────────────────────

/// <summary>The company as its share pages show it, and the cities and localities its listings are in.</summary>
public sealed record GetEstateWebsiteQuery(string Code) : IRequest<EstateWebsiteDto>;

public sealed class GetEstateWebsiteHandler(IWebsiteRepository websites, IPropertyRepository properties, IProjectRepository projects, IEstateSettingsRepository settings,
    TimeProvider clock)
    : IRequestHandler<GetEstateWebsiteQuery, EstateWebsiteDto>
{
    public async Task<EstateWebsiteDto> Handle(GetEstateWebsiteQuery request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var company = site.CompanyId;
        var s = await EstateSettings.LoadAsync(settings, ct, company);
        var fresh = await WebsiteFreshness.LoadAsync(settings, company, clock, ct);
        var listed = await WebsiteListings.Properties(properties, company, fresh).Select(p => new { p.Locality, p.LocalityKey, p.City, p.Purpose }).ToListAsync(ct);
        var listedProjects = await WebsiteListings.Projects(projects, company).Select(p => new { p.Locality, p.LocalityKey, p.City }).ToListAsync(ct);

        var places = listed.Select(p => (p.Locality, p.LocalityKey, p.City, Project: false))
            .Concat(listedProjects.Select(p => (p.Locality, p.LocalityKey, p.City, Project: true))).ToList();
        var cities = places.Select(p => p.City).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var localities = places.GroupBy(p => p.LocalityKey)
            .Select(g => new EstateWebsiteLocalityDto(g.First().Locality, WebsiteSlugs.Slugify(g.First().Locality), g.Select(x => x.City).FirstOrDefault(c => c is not null),
                g.Count(x => !x.Project), g.Count(x => x.Project)))
            .OrderByDescending(l => l.Properties + l.Projects).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new EstateWebsiteDto(site.Code, s.DealerName, s.DealerPhone, s.ReraNumber, cities, localities, listed.Count,
            listed.Count(p => p.Purpose == PropertyPurpose.Sale), listed.Count(p => p.Purpose == PropertyPurpose.Rent), listedProjects.Count);
    }
}

/// <summary>The listed properties, filtered as the website's design filters them, a page at a time (1-based; up to 48 a page).</summary>
public sealed record GetEstateWebsitePropertiesQuery(string Code, EstateWebsitePropertyFilterInput Filter, EstateWebsitePropertySort Sort, int Page, int PageSize)
    : IRequest<EstateWebsitePropertyPageDto>;

public sealed class GetEstateWebsitePropertiesHandler(IWebsiteRepository websites, IPropertyRepository properties, IEstateSettingsRepository settings,
    TimeProvider clock, Requirements.DemandSignals demand, Videos.EstateVideoBook videos)
    : IRequestHandler<GetEstateWebsitePropertiesQuery, EstateWebsitePropertyPageDto>
{
    public async Task<EstateWebsitePropertyPageDto> Handle(GetEstateWebsitePropertiesQuery request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var page = await SearchAsync(site, request, ct);
        // Nothing found (D34): the criteria are counted for the demand report — the first page only, never who searched.
        if (page.TotalCount == 0 && page.Page == 1)
        {
            var f = request.Filter;
            await demand.RecordEmptySearchAsync(site.CompanyId, f.Purpose, f.Types, f.City, f.Locality, f.PriceFrom, f.PriceTo, f.Bhk, ct);
        }
        return page;
    }

    private async Task<EstateWebsitePropertyPageDto> SearchAsync(EstateWebsiteDocument site, GetEstateWebsitePropertiesQuery request, CancellationToken ct)
    {
        var company = site.CompanyId;
        var f = request.Filter;
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 48);
        var empty = new EstateWebsitePropertyPageDto(0, page, size, []);
        var fresh = await WebsiteFreshness.LoadAsync(settings, company, clock, ct);
        var query = WebsiteListings.Properties(properties, company, fresh);

        if (f.Purpose is { } purpose)
            query = query.Where(p => p.Purpose == purpose);
        if (f.Types is { Count: > 0 } types)
        {
            var list = types.ToList();
            query = query.Where(p => list.Contains(p.Type));
        }
        if (f.Commercial is { } commercial)
        {
            var list = WebsiteListings.CommercialTypes.ToList();
            query = commercial ? query.Where(p => list.Contains(p.Type)) : query.Where(p => !list.Contains(p.Type));
        }
        if (f.Plots is { } plots)
        {
            var list = WebsiteListings.PlotTypes.ToList();
            query = plots ? query.Where(p => list.Contains(p.Type)) : query.Where(p => !list.Contains(p.Type));
        }
        if (EstateText.Clean(f.City) is { } cityName)
        {
            // The city as the listings spell it ("mohali" finds "Mohali").
            var cities = (await WebsiteListings.Properties(properties, company, fresh).Select(p => p.City).ToListAsync(ct)).Distinct().ToList();
            if (cities.FirstOrDefault(c => string.Equals(c, cityName, StringComparison.OrdinalIgnoreCase)) is not { } city)
                return empty;
            query = query.Where(p => p.City == city);
        }
        if (EstateText.Clean(f.Locality) is { } locality)
        {
            // By name ("Sector 70") or by slug ("sector-70"), as a "Popular localities" tile sends it.
            var key = EstateText.Key(locality);
            var slug = WebsiteSlugs.Slugify(locality);
            var keys = (await WebsiteListings.Properties(properties, company, fresh).Select(p => new { p.Locality, p.LocalityKey }).ToListAsync(ct))
                .Where(l => l.LocalityKey == key || WebsiteSlugs.Slugify(l.Locality) == slug).Select(l => l.LocalityKey).Distinct().ToList();
            if (keys.Count == 0)
                return empty;
            query = query.Where(p => keys.Contains(p.LocalityKey));
        }
        foreach (var word in (f.Text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
            query = query.Where(p => p.WebsiteText.Contains(word));
        if (f.PriceFrom is { } from)
            query = query.Where(p => p.PriceValue >= from);
        if (f.PriceTo is { } to)
            query = query.Where(p => p.PriceValue <= to);
        if (f.Bhk is { Count: > 0 } bhk)
        {
            var list = bhk.Select(b => (int?)b).ToList();
            query = query.Where(p => list.Contains(p.Bhk));
        }
        if (f.Furnishings is { Count: > 0 } furnishings)
        {
            var list = furnishings.Select(x => (Furnishing?)x).ToList();
            query = query.Where(p => list.Contains(p.Furnishing));
        }
        if (f.Construction is { } construction)
            query = query.Where(p => p.Construction == construction);
        if (f.Facings is { Count: > 0 } facings)
        {
            var list = facings.Select(x => (Facing?)x).ToList();
            query = query.Where(p => list.Contains(p.Facing));
        }
        if (f.Corner is { } corner)
            query = query.Where(p => p.Corner == corner);
        if (f.ParkFacing is { } park)
            query = query.Where(p => p.ParkFacing == park);
        if (f.Featured is { } featured)
            query = query.Where(p => p.Featured == featured);

        var total = await query.CountAsync(ct);
        query = request.Sort switch
        {
            EstateWebsitePropertySort.Newest => query.OrderByDescending(p => p.Id),
            EstateWebsitePropertySort.PriceLow => query.OrderBy(p => p.PriceValue).ThenByDescending(p => p.Id),
            EstateWebsitePropertySort.PriceHigh => query.OrderByDescending(p => p.PriceValue).ThenByDescending(p => p.Id),
            EstateWebsitePropertySort.SizeLarge => query.OrderByDescending(p => p.SizeSqFt).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.Featured).ThenByDescending(p => p.Id),
        };
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new EstateWebsitePropertyPageDto(total, page, size, await WebsiteVideos.MapAsync(rows, videos, company, ct));
    }
}

/// <summary>
/// A property page by its slug: the property while listed; once sold, rented, withdrawn, stale (D6), taken off the
/// website or removed, "no longer available" with similar listings (cms doc 06 §12).
/// </summary>
public sealed record GetEstateWebsitePropertyQuery(string Code, string Slug) : IRequest<EstateWebsitePropertyResultDto>;

public sealed class GetEstateWebsitePropertyHandler(IWebsiteRepository websites, IPropertyRepository properties, IEstateSettingsRepository settings,
    TimeProvider clock, Videos.EstateVideoBook videos)
    : IRequestHandler<GetEstateWebsitePropertyQuery, EstateWebsitePropertyResultDto>
{
    private const int SimilarCount = 4;

    public async Task<EstateWebsitePropertyResultDto> Handle(GetEstateWebsitePropertyQuery request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var company = site.CompanyId;
        var slug = request.Slug.Trim().ToLowerInvariant();
        var p = slug.Length == 0
            ? null
            : (await properties.GetDocuments(x => x.Slug == slug && x.ProjectId == null, true, company).ToListAsync(ct)).OrderBy(x => x.IsDeleted).FirstOrDefault();
        if (p is null)
            return new EstateWebsitePropertyResultDto(EstateWebsiteListingState.NotFound, null, null, []);

        var fresh = await WebsiteFreshness.LoadAsync(settings, company, clock, ct);
        var similar = await SimilarAsync(p, company, fresh, ct);
        return WebsiteListings.IsListed(p, fresh)
            ? new EstateWebsitePropertyResultDto(EstateWebsiteListingState.Listed, p.Title, (await WebsiteVideos.MapAsync([p], videos, company, ct))[0], similar)
            : new EstateWebsitePropertyResultDto(EstateWebsiteListingState.NoLongerAvailable, p.ShowOnWebsite ? p.Title : null, null, similar);
    }

    /// <summary>Listed properties for the same purpose in the same city: the same type first, the same locality, then the nearest price.</summary>
    private async Task<IReadOnlyList<EstateWebsitePropertyDto>> SimilarAsync(PropertyDocument p, string company, WebsiteFreshness fresh, CancellationToken ct)
    {
        var city = p.City;
        var id = p.Id;
        var purpose = p.Purpose;
        var type = p.Type;
        var candidates = WebsiteListings.Properties(properties, company, fresh).Where(x => x.Id != id && x.Purpose == purpose && x.City == city);
        var rows = await candidates.Where(x => x.Type == type).OrderByDescending(x => x.Id).Take(60).ToListAsync(ct);
        if (rows.Count < SimilarCount)
            rows.AddRange(await candidates.Where(x => x.Type != type).OrderByDescending(x => x.Id).Take(60).ToListAsync(ct));
        return await WebsiteVideos.MapAsync([.. rows
            .OrderBy(x => x.Type == type ? 0 : 1)
            .ThenBy(x => x.LocalityKey == p.LocalityKey ? 0 : 1)
            .ThenBy(x => x.PriceValue is { } v && p.PriceValue is { } mine ? Math.Abs(v - mine) : decimal.MaxValue)
            .Take(SimilarCount)], videos, company, ct);
    }
}

/// <summary>The listed projects, featured first, then by name.</summary>
public sealed record GetEstateWebsiteProjectsQuery(string Code) : IRequest<IReadOnlyList<EstateWebsiteProjectSummaryDto>>;

public sealed class GetEstateWebsiteProjectsHandler(IWebsiteRepository websites, IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties)
    : IRequestHandler<GetEstateWebsiteProjectsQuery, IReadOnlyList<EstateWebsiteProjectSummaryDto>>
{
    public async Task<IReadOnlyList<EstateWebsiteProjectSummaryDto>> Handle(GetEstateWebsiteProjectsQuery request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var company = site.CompanyId;
        var rows = await WebsiteListings.Projects(projects, company).OrderByDescending(p => p.Featured).ThenBy(p => p.Name).Take(200).ToListAsync(ct);
        if (rows.Count == 0)
            return [];
        var stock = await ProjectStock.LoadAsync([.. rows.Select(p => p.Id)], company, unitTypes, properties, ct);
        return [.. rows.Select(p =>
        {
            var types = stock.Types[p.Id].ToList();
            return new EstateWebsiteProjectSummaryDto(p.Id, p.Slug ?? string.Empty, p.Name, p.Categories, p.Stage, p.City, p.Locality, p.Photos.FirstOrDefault()?.Url,
                p.LaunchDate, p.PossessionDate, [.. p.Reras.Select(r => r.Number)], [.. types.Select(t => t.Type).Distinct()],
                [.. types.Select(t => t.Bhk).OfType<int>().Distinct().Order()], stock.FromPrice(types), stock.Availability(p.Id), p.Featured);
        })];
    }
}

/// <summary>A listed project by its slug; null when there is none (or it is no longer listed).</summary>
public sealed record GetEstateWebsiteProjectQuery(string Code, string Slug) : IRequest<EstateWebsiteProjectDto?>;

public sealed class GetEstateWebsiteProjectHandler(
    IWebsiteRepository websites, IProjectRepository projects, IUnitTypeRepository unitTypes, IPropertyRepository properties, IPaymentPlanRepository plans,
    Videos.EstateVideoBook videos)
    : IRequestHandler<GetEstateWebsiteProjectQuery, EstateWebsiteProjectDto?>
{
    public async Task<EstateWebsiteProjectDto?> Handle(GetEstateWebsiteProjectQuery request, CancellationToken ct)
    {
        var site = await WebsiteLookup.RequireAsync(websites, request.Code, ct);
        var company = site.CompanyId;
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (slug.Length == 0 || await WebsiteListings.Projects(projects, company).Where(x => x.Slug == slug).FirstOrDefaultAsync(ct) is not { } p)
            return null;

        var stock = await ProjectStock.LoadAsync([p.Id], company, unitTypes, properties, ct);
        var types = stock.Types[p.Id].ToList();
        var planRows = await plans.GetDocuments(x => x.ProjectId == p.Id, false, company).OrderBy(x => x.Name).ToListAsync(ct);

        return new EstateWebsiteProjectDto(p.Id, p.Slug!, p.Name, p.Categories, p.Stage, p.LaunchDate, p.PossessionDate,
            [.. p.Reras.Select(r => new ReraDto(r.Number, r.State, r.RegisteredOn, r.ValidUntil, r.Url))],
            p.City, p.Locality, p.Address, p.Latitude, p.Longitude,
            [.. p.Landmarks.Select(l => new LandmarkDto(l.Name, l.DistanceKm, l.Minutes))], AreaMath.Dto(p.LandArea),
            [.. p.Towers.Select(t => new TowerDto(t.Name, t.Floors))], [.. p.KeyFigures.Select(k => new KeyFigureDto(k.Label, k.Value))],
            p.Overview, p.Highlights, p.Amenities, [.. p.Specifications.Select(x => new SpecificationDto(x.Section, x.Text))],
            [.. p.Photos.Select(x => new PhotoDto(x.Key, x.Url))], p.VideoUrl, p.DroneUrl, ProjectMapper.Media(p.Brochure), ProjectMapper.Photo(p.SitePlan),
            [.. ProjectMapper.Updates(p).Take(12)],
            [.. planRows.Select(x => PlanText.Plan(x, p.Milestones))],
            [.. types.Select(t => new EstateWebsiteUnitTypeDto(t.Name, t.Type, t.Bhk, t.Bathrooms, t.Balconies, AreaMath.Dto(t.CarpetArea), AreaMath.Dto(t.BuiltUpArea),
                AreaMath.Dto(t.SuperArea), AreaMath.Dto(t.PlotArea), t.FrontFeet, t.DepthFeet, ProjectMapper.Photo(t.FloorPlan), stock.FromPrice([t]), t.PriceOnRequest,
                WebsiteListings.Availability(stock.StatusesOf(t.Id))))],
            stock.FromPrice(types), stock.Availability(p.Id), p.Featured)
        {
            Videos = await videos.PublicAsync(Models.Videos.EstateListingKind.Project, p.Id, company, ct),
        };
    }
}

/// <summary>Projects' unit types and their units' statuses and prices — counts and "from" prices, never unit numbers.</summary>
internal sealed class ProjectStock
{
    private sealed record Unit(string? ProjectId, string? UnitTypeId, PropertyStatus Status, decimal? Price);

    private ILookup<string?, Unit> byType = null!;
    private ILookup<string?, Unit> byProject = null!;

    public ILookup<string, UnitTypeDocument> Types { get; private set; } = null!;

    public static async Task<ProjectStock> LoadAsync(IReadOnlyCollection<string> projectIds, string company, IUnitTypeRepository unitTypes,
        IPropertyRepository properties, CancellationToken ct)
    {
        var types = await unitTypes.GetDocuments(t => projectIds.Contains(t.ProjectId), false, company).OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync(ct);
        var units = (await properties.GetDocuments(u => projectIds.Contains(u.ProjectId!), false, company)
                .Select(u => new { u.ProjectId, u.UnitTypeId, u.Status, u.PriceValue }).ToListAsync(ct))
            .Select(u => new Unit(u.ProjectId, u.UnitTypeId, u.Status, u.PriceValue)).ToList();
        return new ProjectStock { Types = types.ToLookup(t => t.ProjectId), byType = units.ToLookup(u => u.UnitTypeId), byProject = units.ToLookup(u => u.ProjectId) };
    }

    public IReadOnlyCollection<PropertyStatus> StatusesOf(string unitTypeId) => [.. byType[unitTypeId].Select(u => u.Status)];

    public EstateWebsiteAvailability Availability(string projectId) => WebsiteListings.Availability([.. byProject[projectId].Select(u => u.Status)]);

    /// <summary>The lowest cost-sheet price (or own price) of an available unit whose type does not hide its price (D22).</summary>
    public decimal? FromPrice(IEnumerable<UnitTypeDocument> types) =>
        types.Where(t => !t.PriceOnRequest).SelectMany(t => byType[t.Id]).Where(u => u.Status == PropertyStatus.Available && u.Price is not null).Select(u => u.Price).Min();
}

/// <summary>A payment plan as text for the website (cms doc 06 §12): "10% on booking", "₹5,00,000 within 30 days of booking".</summary>
internal static class PlanText
{
    public static EstateWebsitePaymentPlanDto Plan(PaymentPlanDocument plan, IReadOnlyList<MilestoneModel> milestones) => new(plan.Name, plan.Kind,
        [.. plan.Steps.Select(s =>
        {
            var milestone = s.MilestoneId is { } id ? milestones.FirstOrDefault(m => m.Id == id)?.Name : null;
            var part = s.Amount is { } amount ? EstateText.Rupees(amount) : $"{(s.Percent ?? 0m).ToString("0.##", CultureInfo.InvariantCulture)}%";
            var due = s.Due switch
            {
                StepDue.OnBooking => "on booking",
                StepDue.DaysAfterBooking => $"within {s.Days ?? 0} days of booking",
                StepDue.AtMilestone => milestone is null ? "at a construction milestone" : $"at {milestone}",
                StepDue.OnPossession => "on possession",
                _ => "on the agreement for sale",
            };
            return new EstateWebsitePlanStepDto(s.Label, s.Percent, s.Amount, s.Due, s.Days, milestone, $"{part} {due}");
        })]);
}
