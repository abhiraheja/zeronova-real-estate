using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Website;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The Real Estate website's listings (cms doc 06 §12, E2): the website code (made on first ask, unique across companies),
/// Show on website and Featured, slugs made once, the anonymous queries by code with the design's filters, what is
/// listed (website on, the flag, available or on hold, not stale, never a unit on its own), a sold property's page, unit-type
/// availability, and that owners, unit numbers, notes and CP terms never reach the website.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class WebsiteTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345")));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    /// <summary>As the site's server: nobody signed in.</summary>
    private Task<T> Public<T>(IRequest<T> request)
    {
        T result = default!;
        return host.RunAsJobAsync(async sp => result = await sp.GetRequiredService<IMediator>().Send(request)).ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result;
        });
    }

    private async Task<string> WebsiteOn(string company = CompanyA) =>
        (await host.SendAs(company, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, null)))).Code;

    private Task<PropertyDto> Add(PropertyInput input) => host.SendAs(CompanyA, new SavePropertyCommand(null, input));

    private static PropertyInput Flat(string locality = "Sector 70", decimal price = 9500000m, bool show = true, int bhk = 3, string? notes = null) => new()
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = locality, Society = "Gillco Heights", Block = "Tower C",
        UnitNumber = "C-1204", Bhk = bhk, Bathrooms = 3, Floor = 12, TotalFloors = 14, Furnishing = Furnishing.SemiFurnished,
        Construction = ConstructionStatus.Ready, SuperArea = new AreaInput(1650, AreaUnit.SqFt), Price = price, OwnerExpectation = 8800000m,
        Facing = Facing.East, Photos = [new PhotoInput("real-estate/p/f.jpg", "https://cdn.test/f.jpg")], Notes = notes ?? "Owner divorcing, needs quick sale",
        Source = PropertySource.OtherDealer, SourceDealerName = "Raj Estates", SourceDealerPhone = "9811122233", ShowOnWebsite = show,
    };

    private static PropertyInput Plot(string locality = "Sector 82", decimal price = 12000000m) => new()
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, City = "Mohali", Locality = locality, PlotArea = new AreaInput(200, AreaUnit.SqYd),
        Price = price, Corner = true, ParkFacing = true, Facing = Facing.North, ShowOnWebsite = true,
    };

    private static PropertyInput Shop() => new()
    {
        Purpose = PropertyPurpose.Rent, Type = PropertyType.Shop, City = "Zirakpur", Locality = "VIP Road", BuiltUpArea = new AreaInput(400, AreaUnit.SqFt),
        Rent = 45000m, ShowOnWebsite = true, Featured = true,
    };

    private static ProjectInput Own(string name = "Aetro Sapphire", bool show = true) => new()
    {
        Name = name, Kind = ProjectKind.Own, Locality = "Sunam Road", City = "Sunam", Categories = [ProjectCategory.Commercial], Stage = ProjectStage.UnderConstruction,
        Reras = [new ReraInput("PBRERA-SGR78-PC0365", "Punjab", null, null, null)], Overview = "50 showrooms on a 201 ft front", GstPercent = 12m,
        KeyFigures = [new KeyFigureInput("Showrooms", "50")], Photos = [new PhotoInput("real-estate/pj/1.jpg", "https://cdn.test/pj1.jpg")],
        Notes = "Internal: land deal pending with Bansal", ShowOnWebsite = show,
    };

    // ── The website code ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_website_code_is_made_once_unique_across_companies_and_can_be_changed_by_settings()
    {
        var first = await host.SendAs(CompanyA, new GetEstateWebsiteSettingsQuery());
        Assert.False(first.Enabled);
        Assert.Matches("^[A-Z2-9]{6}$", first.Code);
        Assert.Equal(first.Code, (await host.SendAs(CompanyA, new GetEstateWebsiteSettingsQuery())).Code);
        var other = await host.SendAs(CompanyB, new GetEstateWebsiteSettingsQuery());
        Assert.NotEqual(first.Code, other.Code);

        // Only letters and digits, 4 to 20; kept in upper case; another company's code is taken.
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyB, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(null, "WP-1"))));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyB, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(null, "ABC"))));
        Assert.Contains("taken", (await Assert.ThrowsAsync<ValidatorException>(() =>
            host.SendAs(CompanyB, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(null, first.Code.ToLowerInvariant()))))).Message);
        var renamed = await host.SendAs(CompanyB, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, "worldpro1")));
        Assert.Equal((true, "WORLDPRO1"), (renamed.Enabled, renamed.Code));

        // Null keeps.
        var kept = await host.SendAs(CompanyB, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(null, null)));
        Assert.Equal((true, "WORLDPRO1"), (kept.Enabled, kept.Code));
        Assert.Equal("WORLDPRO1", (await Public(new GetEstateWebsiteQuery(" worldpro1 "))).Code);

        var names = new List<string>();
        await host.WithAsync(CompanyA, async sp =>
        {
            var db = sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
            foreach (var collection in new[] { "re_websites", "re_properties", "re_projects" })
                names.AddRange(MongoDB.Driver.IAsyncCursorExtensions.ToList(await db.GetCollection<MongoDB.Bson.BsonDocument>(collection).Indexes.ListAsync())
                    .Select(i => $"{collection}.{i["name"].AsString}"));
        });
        Assert.Contains("re_websites.code_unique", names);
        Assert.Contains("re_properties.company_slug_unique", names);
        Assert.Contains("re_projects.company_slug_unique", names);
    }

    // ── Flags and slugs ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flags_are_off_by_default_null_keeps_them_and_the_slug_is_made_once()
    {
        var plain = await Add(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, Price = 9500000m,
        });
        Assert.Equal((false, false), (plain.ShowOnWebsite, plain.Featured));
        Assert.Equal("3-bhk-flat-for-sale-sector-70-mohali-p0001", plain.WebsiteSlug);

        var shown = await host.SendAs(CompanyA, new SavePropertyCommand(plain.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, ShowOnWebsite = true, Featured = true,
        }));
        Assert.Equal((true, true), (shown.ShowOnWebsite, shown.Featured));

        // A new title (4 BHK, another sector) keeps the slug; flags left null stay as they were.
        var changed = await host.SendAs(CompanyA, new SavePropertyCommand(plain.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 71", Bhk = 4,
        }));
        Assert.Equal("4 BHK Flat in Sector 71", changed.Title);
        Assert.Equal(("3-bhk-flat-for-sale-sector-70-mohali-p0001", true, true), (changed.WebsiteSlug, changed.ShowOnWebsite, changed.Featured));

        var plot = await Add(Plot());
        var shop = await Add(Shop());
        Assert.Equal("200-sq-yd-corner-plot-for-sale-sector-82-mohali-p0002", plot.WebsiteSlug);
        Assert.Equal("shop-for-rent-vip-road-zirakpur-p0003", shop.WebsiteSlug);

        // Projects: from the name; the code is added when another project has the name. Kept on a rename.
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own(show: false)));
        Assert.Equal(("aetro-sapphire", false, false), (project.WebsiteSlug, project.ShowOnWebsite, project.Featured));
        var twin = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own(show: false)));
        Assert.Equal("aetro-sapphire-pj0002", twin.WebsiteSlug);
        var renamed = await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, new ProjectInput
        {
            Name = "Aetro Emerald", Kind = ProjectKind.Own, Locality = "Sunam Road", Reras = Own().Reras, Featured = true,
        }));
        Assert.Equal(("aetro-sapphire", false, true), (renamed.WebsiteSlug, renamed.ShowOnWebsite, renamed.Featured));
        var summary = Assert.Single(await host.SendAs(CompanyA, new GetProjectsQuery(new ProjectSearchInput { Text = "emerald" })));
        Assert.Equal((false, true), (summary.ShowOnWebsite, summary.Featured));

        // A unit is never listed on its own: units get no slug and no flag (and the property save refuses them).
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput { Name = "Showroom", Type = PropertyType.Showroom }));
        var unit = Assert.Single(await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = type.Id, Count = 1, Pattern = "SCO-{n}",
        })));
        var unitProperty = await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id));
        Assert.Equal(((string?)null, false), (unitProperty.WebsiteSlug, unitProperty.ShowOnWebsite));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SavePropertyCommand(unit.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Showroom, Locality = "Sunam Road", ShowOnWebsite = true,
        })));
    }

    [Fact]
    public async Task An_own_project_goes_on_the_website_only_with_its_rera_registration()
    {
        var unregistered = new ProjectInput { Name = "Farm Estates", Kind = ProjectKind.Own, Locality = "Sector 82", ShowOnWebsite = true };
        Assert.Contains("RERA", (await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveProjectCommand(null, unregistered)))).Message);
        var exempt = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Farm Estates", Kind = ProjectKind.Own, Locality = "Sector 82", ShowOnWebsite = true, ReraNotRequired = true, ReraExemptReason = "Under 500 sq m",
        }));
        Assert.True(exempt.ShowOnWebsite);
        var partner = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, BuilderName = "GV Builders", Locality = "Sector 115", ShowOnWebsite = true,
        }));
        Assert.True(partner.ShowOnWebsite);
    }

    // ── What is listed ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nothing_is_listed_with_the_website_off_or_a_wrong_code_and_only_flagged_available_or_on_hold_listings_with_it_on()
    {
        var code = (await host.SendAs(CompanyA, new GetEstateWebsiteSettingsQuery())).Code;
        await Add(Flat());
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new GetEstateWebsiteQuery(code)));   // website off
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Featured, 1, 12)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new GetEstateWebsiteQuery("NOSUCH1")));

        await WebsiteOn();
        var hidden = await Add(Flat("Sector 71", show: false));
        var onHold = await Add(Flat("Sector 72"));
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(onHold.Id, new ConfirmPropertyInput(PropertyStatus.OnHold, null, ConfirmedVia.Call, "2099-01-01")));
        var sold = await Add(Flat("Sector 73"));
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(sold.Id, new ConfirmPropertyInput(PropertyStatus.Sold, null, ConfirmedVia.Call, null)));
        var negotiating = await Add(Flat("Sector 74"));
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(negotiating.Id, new ConfirmPropertyInput(PropertyStatus.UnderNegotiation, null, ConfirmedVia.Call, null)));
        await host.SendAs(CompanyB, new SavePropertyCommand(null, Flat("Sector 70")));   // another company's

        var page = await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 1, 12));
        Assert.Equal(2, page.TotalCount);
        // On hold is listed — and shown to the public as available (a hold is the dealer's business).
        Assert.Equal([("Sector 72", PropertyStatus.Available), ("Sector 70", PropertyStatus.Available)], page.Items.Select(i => (i.Locality, i.Status)));
        Assert.DoesNotContain(page.Items, i => i.Slug == hidden.WebsiteSlug);

        // A project's units never show on their own, even with the project on the website.
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput { Name = "Showroom", Type = PropertyType.Showroom }));
        await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput { ProjectId = project.Id, UnitTypeId = type.Id, Count = 3, Pattern = "SCO-{n}" }));
        Assert.Equal(2, (await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 1, 12))).TotalCount);
        Assert.Single(await Public(new GetEstateWebsiteProjectsQuery(code)));

        // Off again: gone.
        await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(false, null)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new GetEstateWebsiteProjectsQuery(code)));
        var settings = await host.SendAs(CompanyA, new GetEstateWebsiteSettingsQuery());
        Assert.Equal((false, 2, 1), (settings.Enabled, settings.ListedProperties, settings.ListedProjects));
    }

    [Fact]
    public async Task A_stale_property_is_not_on_the_website_until_it_is_confirmed_again()
    {
        var code = await WebsiteOn();
        var fresh = await Add(Flat("Sector 70"));
        var stale = await Add(Flat("Sector 71"));
        var shop = await Add(Shop());
        await Backdate(stale.Id, 31);   // past the 30 days for sale
        await Backdate(shop.Id, 16);    // past the 15 days for rent
        var keptShop = await Add(Shop());
        await Backdate(keptShop.Id, 14);

        var page = await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 1, 12));
        Assert.Equal([keptShop.Code, fresh.Code], page.Items.Select(i => i.Code));
        Assert.Equal(2, (await Public(new GetEstateWebsiteQuery(code))).Properties);
        Assert.Equal(2, (await host.SendAs(CompanyA, new GetEstateWebsiteSettingsQuery())).ListedProperties);
        var gone = await Public(new GetEstateWebsitePropertyQuery(code, stale.WebsiteSlug!));
        Assert.Equal((EstateWebsiteListingState.NoLongerAvailable, (EstateWebsitePropertyDto?)null), (gone.State, gone.Property));
        Assert.DoesNotContain(gone.Similar, s => s.Code == shop.Code);
        Assert.True((await host.SendAs(CompanyA, new GetPropertyQuery(stale.Id))).ShowOnWebsite);   // the flag stays; only the listing waits

        await host.SendAs(CompanyA, new ConfirmPropertyCommand(stale.Id, new ConfirmPropertyInput(PropertyStatus.Available, null, ConfirmedVia.Call, null)));
        Assert.Equal(EstateWebsiteListingState.Listed, (await Public(new GetEstateWebsitePropertyQuery(code, stale.WebsiteSlug!))).State);
        Assert.Equal(3, (await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 1, 12))).TotalCount);
    }

    private Task Backdate(string propertyId, int days) => host.WithAsync(CompanyA, async sp =>
    {
        var repo = sp.GetRequiredService<IPropertyRepository>();
        var doc = await repo.GetDocument(propertyId).FirstAsync();
        doc.LastConfirmedAt = DateTime.UtcNow.AddDays(-days);
        await repo.UpdateAsync(doc);
    });

    [Fact]
    public async Task The_website_names_the_company_as_share_pages_do_with_the_cities_and_localities_in_use()
    {
        var code = await WebsiteOn();
        await Add(Flat("Sector 70"));
        await Add(Flat("sector  70", price: 8000000m));
        await Add(Plot("Sector 82"));
        await Add(Shop());
        await Add(Flat("Sector 99", show: false));
        await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));

        var site = await Public(new GetEstateWebsiteQuery(code));
        Assert.Equal((code, "Sharma Properties", "919876012345", "PBRERA-SAS79-REA0123"), (site.Code, site.CompanyName, site.Phone, site.ReraNumber));
        Assert.Equal((4, 3, 1, 1), (site.Properties, site.ForSale, site.ForRent, site.Projects));
        Assert.Equal(["Mohali", "Sunam", "Zirakpur"], site.Cities);
        Assert.Equal(("Sector 70", "sector-70", "Mohali", 2, 0), site.Localities[0] is var l ? (l.Name, l.Slug, l.City, l.Properties, l.Projects) : default);
        Assert.Contains(site.Localities, x => x is { Name: "Sunam Road", Projects: 1 });
        Assert.DoesNotContain(site.Localities, x => x.Name == "Sector 99");
    }

    [Fact]
    public async Task The_listing_filters_sorts_and_pages_as_the_design_does()
    {
        var code = await WebsiteOn();
        var a = await Add(Flat("Sector 70", 9500000m));
        var b = await Add(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 71", Bhk = 2, Price = 6000000m,
            Furnishing = Furnishing.Unfurnished, Construction = ConstructionStatus.UnderConstruction, Facing = Facing.West, ShowOnWebsite = true, Featured = true,
        });
        var plot = await Add(Plot());
        var shop = await Add(Shop());

        async Task<IReadOnlyList<string>> Find(EstateWebsitePropertyFilterInput filter, EstateWebsitePropertySort sort = EstateWebsitePropertySort.Newest) =>
            [.. (await Public(new GetEstateWebsitePropertiesQuery(code, filter, sort, 1, 12))).Items.Select(i => i.Code)];

        Assert.Equal([shop.Code], await Find(new() { Purpose = PropertyPurpose.Rent }));
        Assert.Equal([b.Code, a.Code], await Find(new() { Types = [PropertyType.Flat] }));
        Assert.Equal([shop.Code], await Find(new() { Commercial = true }));
        Assert.Equal([plot.Code, b.Code, a.Code], await Find(new() { Commercial = false }));
        Assert.Equal([plot.Code], await Find(new() { Plots = true }));
        Assert.Equal([shop.Code], await Find(new() { City = "zirakpur" }));
        Assert.Empty(await Find(new() { City = "Ludhiana" }));
        Assert.Equal([a.Code], await Find(new() { Locality = "sector-70" }));
        Assert.Equal([a.Code], await Find(new() { Locality = "Sector 70" }));
        Assert.Equal([a.Code], await Find(new() { Text = "gillco" }));
        Assert.Empty(await Find(new() { Text = "divorcing" }));   // notes are never searched
        Assert.Empty(await Find(new() { Text = "c-1204" }));      // nor the unit number
        Assert.Equal([b.Code, a.Code], await Find(new() { PriceFrom = 5000000m, PriceTo = 10000000m }));
        Assert.Equal([b.Code], await Find(new() { Bhk = [2] }));
        Assert.Equal([a.Code], await Find(new() { Furnishings = [Furnishing.SemiFurnished] }));
        Assert.Equal([b.Code], await Find(new() { Construction = ConstructionStatus.UnderConstruction }));
        Assert.Equal([plot.Code], await Find(new() { Facings = [Facing.North] }));
        Assert.Equal([plot.Code], await Find(new() { Corner = true }));
        Assert.Equal([plot.Code], await Find(new() { ParkFacing = true }));
        Assert.Equal([shop.Code, b.Code], await Find(new() { Featured = true }));

        Assert.Equal([shop.Code, b.Code, plot.Code, a.Code], await Find(new(), EstateWebsitePropertySort.Featured));
        Assert.Equal([shop.Code, b.Code, a.Code, plot.Code], await Find(new(), EstateWebsitePropertySort.PriceLow));
        Assert.Equal(plot.Code, (await Find(new(), EstateWebsitePropertySort.PriceHigh))[0]);

        var second = await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 2, 3));
        Assert.Equal((4, 2, 3, a.Code), (second.TotalCount, second.Page, second.PageSize, Assert.Single(second.Items).Code));
    }

    [Fact]
    public async Task A_property_page_by_slug_and_a_sold_one_says_no_longer_available_with_similar_ones()
    {
        var code = await WebsiteOn();
        var main = await Add(Flat("Sector 70", 9500000m));
        var near = await Add(Flat("Sector 70", 9000000m));
        var far = await Add(Flat("Sector 80", 9400000m));
        await Add(Plot());
        await host.SendAs(CompanyA, new SavePropertyCommand(null, Flat("Sector 70", show: false)));

        var page = await Public(new GetEstateWebsitePropertyQuery(code, main.WebsiteSlug!.ToUpperInvariant()));
        Assert.Equal((EstateWebsiteListingState.Listed, main.Title), (page.State, page.Property!.Title));
        Assert.Equal([near.Code, far.Code], page.Similar.Take(2).Select(s => s.Code));   // the same locality first
        Assert.DoesNotContain(page.Similar, s => s.Code == main.Code);

        await host.SendAs(CompanyA, new ConfirmPropertyCommand(main.Id, new ConfirmPropertyInput(PropertyStatus.Sold, null, ConfirmedVia.Call, null)));
        var gone = await Public(new GetEstateWebsitePropertyQuery(code, main.WebsiteSlug));
        Assert.Equal((EstateWebsiteListingState.NoLongerAvailable, main.Title, (EstateWebsitePropertyDto?)null), (gone.State, gone.Title, gone.Property));
        Assert.Equal(near.Code, gone.Similar[0].Code);

        // Taken off the website (never mind why): no title; removed: still "no longer available"; unknown: not found.
        await host.SendAs(CompanyA, new SavePropertyCommand(far.Id, Flat("Sector 80", 9400000m, show: false)));
        var off = await Public(new GetEstateWebsitePropertyQuery(code, far.WebsiteSlug!));
        Assert.Equal((EstateWebsiteListingState.NoLongerAvailable, (string?)null), (off.State, off.Title));
        await host.SendAs(CompanyA, new DeletePropertyCommand(near.Id));
        Assert.Equal(EstateWebsiteListingState.NoLongerAvailable, (await Public(new GetEstateWebsitePropertyQuery(code, near.WebsiteSlug!))).State);
        var unknown = await Public(new GetEstateWebsitePropertyQuery(code, "no-such-place-p9999"));
        Assert.Equal((EstateWebsiteListingState.NotFound, 0), (unknown.State, unknown.Similar.Count));
        // Another company's slug is not this website's.
        var otherCode = await WebsiteOn(CompanyB);
        Assert.Equal(EstateWebsiteListingState.NotFound, (await Public(new GetEstateWebsitePropertyQuery(otherCode, page.Similar[0].Slug))).State);
    }

    // ── Projects ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_project_shows_its_facts_unit_types_with_from_prices_and_availability_and_plans_as_text()
    {
        var code = await WebsiteOn();
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));
        var showroom = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "Ground floor showroom", Type = PropertyType.Showroom, BuiltUpArea = new AreaInput(1000, AreaUnit.SqFt),
        }));
        var booth = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "Booth", Type = PropertyType.Shop, BuiltUpArea = new AreaInput(200, AreaUnit.SqFt),
        }));
        var corner = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "Corner SCO", Type = PropertyType.Showroom, BuiltUpArea = new AreaInput(2500, AreaUnit.SqFt), PriceOnRequest = true,
        }));
        await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput { Name = "Upcoming plaza", Type = PropertyType.Shop }));
        var showrooms = await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = showroom.Id, Count = 20, Pattern = "SCO-{n}",
        }));
        var booths = await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = booth.Id, Count = 2, Pattern = "B-{n}",
        }));
        var corners = await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = project.Id, UnitTypeId = corner.Id, Count = 5, Pattern = "C-{n}",
        }));
        await host.SendAs(CompanyA, new SavePriceListCommand(project.Id, new PriceListInput(
        [
            new UnitTypeRateInput(showroom.Id, RateBasis.PerSqFt, 10000m, AreaBasis.BuiltUp), new UnitTypeRateInput(booth.Id, RateBasis.LumpSum, 2500000m),
            new UnitTypeRateInput(corner.Id, RateBasis.LumpSum, 40000000m),
        ], null, null, null)));
        // 20 showrooms: 17 booked or blocked, 1 withdrawn → 2 left of 19 → few left (≤ 3). Both booths sold. 5 corners, all available.
        foreach (var unit in showrooms.Take(17))
            await host.SendAs(CompanyA, new SetUnitStatusCommand(unit.Id, new UnitStatusInput(UnitStatus.Booked, null, "lead-9", "Kamal Bansal")));
        await host.SendAs(CompanyA, new SetUnitStatusCommand(showrooms[17].Id, new UnitStatusInput(UnitStatus.Withdrawn, null, null, null)));
        foreach (var unit in booths)
            await host.SendAs(CompanyA, new SetUnitStatusCommand(unit.Id, new UnitStatusInput(UnitStatus.Sold, null, null, null)));
        await host.SendAs(CompanyA, new SavePaymentPlanCommand(project.Id, null, new PaymentPlanInput("Construction linked", PaymentPlanKind.ConstructionLinked,
        [
            new PlanStepInput("Booking", 10m, null, StepDue.OnBooking, null, null),
            new PlanStepInput("Agreement", 20m, null, StepDue.OnAgreement, null, null),
            new PlanStepInput("Within a month", null, 500000m, StepDue.DaysAfterBooking, 30, null),
            new PlanStepInput("Possession", 70m, null, StepDue.OnPossession, null, null),
        ], "CP gets 2% on this plan")));

        var list = Assert.Single(await Public(new GetEstateWebsiteProjectsQuery(code)));
        Assert.Equal(("aetro-sapphire", "Aetro Sapphire", "https://cdn.test/pj1.jpg", (decimal?)11200000m), (list.Slug, list.Name, list.CoverUrl, list.FromPrice));
        Assert.Equal(["PBRERA-SGR78-PC0365"], list.ReraNumbers);
        Assert.Equal(EstateWebsiteAvailability.Available, list.Availability);   // 7 of 26 counted units left

        var page = await Public(new GetEstateWebsiteProjectQuery(code, "Aetro-Sapphire"));
        Assert.NotNull(page);
        Assert.Equal(("50 showrooms on a 201 ft front", "Showrooms"), (page!.Overview, page.KeyFigures[0].Label));
        var types = page.UnitTypes.ToDictionary(t => t.Name);
        Assert.Equal(((decimal?)11200000m, EstateWebsiteAvailability.FewLeft), (types["Ground floor showroom"].FromPrice, types["Ground floor showroom"].Availability));
        Assert.Equal(((decimal?)null, EstateWebsiteAvailability.SoldOut), (types["Booth"].FromPrice, types["Booth"].Availability));
        Assert.Equal(((decimal?)null, true, EstateWebsiteAvailability.Available),
            (types["Corner SCO"].FromPrice, types["Corner SCO"].PriceOnRequest, types["Corner SCO"].Availability));
        Assert.Equal(EstateWebsiteAvailability.Available, types["Upcoming plaza"].Availability);   // no units made yet
        var plan = Assert.Single(page.PaymentPlans);
        Assert.Equal(["10% on booking", "20% on the agreement for sale", "₹5,00,000 within 30 days of booking", "70% on possession"], plan.Steps.Select(s => s.Text));

        Assert.Null(await Public(new GetEstateWebsiteProjectQuery(code, "no-such-project")));
        await host.SendAs(CompanyA, new SaveProjectCommand(project.Id, Own(show: false)));
        Assert.Null(await Public(new GetEstateWebsiteProjectQuery(code, "aetro-sapphire")));
        Assert.Empty(await Public(new GetEstateWebsiteProjectsQuery(code)));
    }

    [Fact]
    public void Availability_buckets_follow_the_threshold()
    {
        static EstateWebsiteAvailability Of(int available, int taken, int withdrawn = 0) => WebsiteListings.Availability(
            [.. Enumerable.Repeat(PropertyStatus.Available, available), .. Enumerable.Repeat(PropertyStatus.Sold, taken),
                .. Enumerable.Repeat(PropertyStatus.Withdrawn, withdrawn)]);

        Assert.Equal(EstateWebsiteAvailability.Available, WebsiteListings.Availability([]));
        Assert.Equal(EstateWebsiteAvailability.SoldOut, Of(0, 10));
        Assert.Equal(EstateWebsiteAvailability.SoldOut, Of(0, 0, 4));
        Assert.Equal(EstateWebsiteAvailability.FewLeft, Of(1, 0));
        Assert.Equal(EstateWebsiteAvailability.FewLeft, Of(3, 2));
        Assert.Equal(EstateWebsiteAvailability.Available, Of(4, 6));
        Assert.Equal(EstateWebsiteAvailability.FewLeft, Of(10, 90));     // 10% of 100
        Assert.Equal(EstateWebsiteAvailability.Available, Of(11, 89));
        Assert.Equal(EstateWebsiteAvailability.FewLeft, Of(3, 5, 100));  // withdrawn units do not count
    }

    // ── Never shown ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owners_unit_numbers_notes_cp_terms_and_holds_never_reach_the_website()
    {
        var code = await WebsiteOn();
        var flat = await Add(Flat(notes: "Owner divorcing, needs quick sale"));
        var owner = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = "Harjinder Singh", Phone = "9888877777", Email = "harjinder@example.test", Address = "House 12, Phase 3B2",
        }));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));

        var partner = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Green Valley", Kind = ProjectKind.Partner, BuilderName = "GV Builders", BuilderContactName = "Mr Gill", BuilderPhone = "9812300000",
            BuilderEmail = "gill@gv.test", Locality = "Sector 115", City = "Mohali", Notes = "Builder slow on payouts",
            CpTerms = new CpTermsInput(2.5m, null, "After 20 percent collected", 90, null, "Bonus 50000 on 5 bookings"), ShowOnWebsite = true,
        }));
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(partner.Id, null, new UnitTypeInput
        {
            Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1500, AreaUnit.SqFt),
        }));
        var units = await host.SendAs(CompanyA, new GenerateUnitsCommand(new GenerateUnitsInput
        {
            ProjectId = partner.Id, UnitTypeId = type.Id, Tower = "T7", FloorFrom = 1, FloorTo = 2, UnitsPerFloor = 2, Pattern = "{tower}-{floor}{nn}",
        }));
        await host.SendAs(CompanyA, new SetUnitStatusCommand(units[0].Id, new UnitStatusInput(UnitStatus.Blocked, "2099-01-01", "lead-77", "Navdeep Kaur")));
        await host.SendAs(CompanyA, new SavePaymentPlanCommand(partner.Id, null, new PaymentPlanInput("Down payment", PaymentPlanKind.DownPayment,
            [new PlanStepInput("Booking", 100m, null, StepDue.OnBooking, null, null)], "Secret plan note")));

        var json = string.Join("\n", new[]
        {
            JsonSerializer.Serialize(await Public(new GetEstateWebsiteQuery(code))),
            JsonSerializer.Serialize(await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Featured, 1, 48))),
            JsonSerializer.Serialize(await Public(new GetEstateWebsitePropertyQuery(code, flat.WebsiteSlug!))),
            JsonSerializer.Serialize(await Public(new GetEstateWebsiteProjectsQuery(code))),
            JsonSerializer.Serialize(await Public(new GetEstateWebsiteProjectQuery(code, partner.WebsiteSlug!))),
        });
        Assert.Contains(flat.Title, json);
        Assert.Contains("Green Valley", json);
        foreach (var secret in new[]
                 {
                     "C-1204", "Tower C", "Harjinder", "919888877777", "harjinder@", "Phase 3B2", "8800000", "divorcing", "Raj Estates", "919811122233",
                     "T7-1", "T7-2", "GV Builders", "Mr Gill", "919812300000", "gill@gv.test", "After 20 percent", "Bonus 50000", "Builder slow", "Secret plan note",
                     "Navdeep", "lead-77", "2099-01-01",
                 })
            Assert.DoesNotContain(secret, json);
    }

    [Fact]
    public async Task Listings_carry_their_own_ids_and_the_public_description_but_never_the_notes()
    {
        var code = await WebsiteOn();
        var flat = await Add(Flat());
        var described = await host.SendAs(CompanyA, new SavePropertyCommand(flat.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, Price = 9500000m, ShowOnWebsite = true,
            Notes = "Owner divorcing, needs quick sale", Description = "  Sunny corner flat, 2 min from the market.  ",
        }));
        Assert.Equal("Sunny corner flat, 2 min from the market.", described.Description);
        // Null keeps it; empty clears it.
        var kept = await host.SendAs(CompanyA, new SavePropertyCommand(flat.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, Price = 9500000m, ShowOnWebsite = true,
            Notes = "Owner divorcing, needs quick sale",
        }));
        Assert.Equal(described.Description, kept.Description);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SavePropertyCommand(flat.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "Sector 70", Description = new string('x', 2001),
        })));

        var item = Assert.Single((await Public(new GetEstateWebsitePropertiesQuery(code, new(), EstateWebsitePropertySort.Newest, 1, 12))).Items);
        Assert.Equal((flat.Id, "Sunny corner flat, 2 min from the market."), (item.Id, item.Description));
        var page = await Public(new GetEstateWebsitePropertyQuery(code, kept.WebsiteSlug!));
        Assert.Equal(flat.Id, page.Property!.Id);
        var share = await host.SendAs(CompanyA, new Application.Features.Visits.CreateShareCommand(flat.Id, null));
        var shared = await Public(new Application.Features.Visits.GetSharedPropertyQuery(share.Token));
        Assert.Equal("Sunny corner flat, 2 min from the market.", shared.Description);
        foreach (var json in new[] { JsonSerializer.Serialize(item), JsonSerializer.Serialize(page), JsonSerializer.Serialize(shared) })
            Assert.DoesNotContain("divorcing", json);

        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, Own()));
        var summary = Assert.Single(await Public(new GetEstateWebsiteProjectsQuery(code)));
        Assert.Equal(project.Id, summary.Id);
        var projectPage = await Public(new GetEstateWebsiteProjectQuery(code, summary.Slug));
        Assert.Equal(project.Id, projectPage!.Id);
        Assert.DoesNotContain("Bansal", JsonSerializer.Serialize(projectPage));
    }
}
