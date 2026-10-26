using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// M1 properties: Indian fields and units, codes, the title, the other types' fields left out, search, owners and their
/// shares, owner details only with POWV, duplicates, the dealer's own fields, staleness and confirming, tenancy.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class PropertyTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<PropertyDto> Add(PropertyInput input, string company = CompanyA) => host.SendAs(company, new SavePropertyCommand(null, input));

    private static PropertyInput Plot(string locality = "Sector 70", decimal gaj = 200, decimal price = 12000000m) => new()
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, City = "Mohali", Locality = locality, PlotArea = new AreaInput(gaj, AreaUnit.SqYd),
        Price = price, Facing = Facing.East, Corner = true, ParkFacing = true, RoadWidthFeet = 40, Tenure = LandTenure.Freehold,
        Bhk = 3,   // not a plot's: dropped
    };

    [Fact]
    public async Task A_plot_gets_a_code_a_title_and_its_size_in_sq_ft_and_drops_a_flats_fields()
    {
        var plot = await Add(Plot());
        var second = await Add(Plot("Sector 71"));

        Assert.Equal(("P-0001", "P-0002"), (plot.Code, second.Code));
        Assert.Equal("200 sq yd Corner Plot in Sector 70", plot.Title);
        Assert.Equal(1800m, plot.SizeSqFt);
        Assert.Equal(6667m, plot.RatePerSqFt);
        Assert.Null(plot.Bhk);
        Assert.Equal((PropertyStatus.Available, false, ConfirmedVia.Added), (plot.Status, plot.Stale, plot.ConfirmedVia));
        Assert.Empty((await host.SendAs(CompanyB, new GetPropertiesQuery(new PropertySearchInput()))).Items);
    }

    [Fact]
    public async Task Regional_units_use_the_dealers_local_value()
    {
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(225m, 4500m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Ludhiana", "PBRERA-A-123")));
        var plot = await Add(new PropertyInput { Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Model Town", PlotArea = new AreaInput(10, AreaUnit.Marla) });

        Assert.Equal((2250m, "Ludhiana"), (plot.SizeSqFt, plot.City));
        Assert.Equal("10 marla Plot in Model Town", plot.Title);
    }

    [Fact]
    public async Task Search_filters_by_purpose_locality_price_bhk_facing_corner_and_text()
    {
        await Add(Plot());
        await Add(Plot("Sector 71", 300, 18000000m));
        await Add(new PropertyInput { Purpose = PropertyPurpose.Rent, Type = PropertyType.Flat, Locality = "Sector 70", Bhk = 3, Rent = 35000, Furnishing = Furnishing.SemiFurnished, Society = "Beverly Golf Avenue" });

        Assert.Equal(2, (await Search(new PropertySearchInput { Purpose = PropertyPurpose.Sale })).TotalCount);
        Assert.Single((await Search(new PropertySearchInput { Localities = ["sector 70"], Purpose = PropertyPurpose.Sale })).Items);
        Assert.Single((await Search(new PropertySearchInput { PriceMax = 15000000m, Purpose = PropertyPurpose.Sale })).Items);
        Assert.Single((await Search(new PropertySearchInput { Bhk = [3] })).Items);
        Assert.Equal(2, (await Search(new PropertySearchInput { Facings = [Facing.East], Corner = true })).TotalCount);
        Assert.Equal("Beverly Golf Avenue", Assert.Single((await Search(new PropertySearchInput { Text = "beverly golf" })).Items).Society);
        Assert.Equal(18000000m, (await Search(new PropertySearchInput { Purpose = PropertyPurpose.Sale, Sort = PropertySort.PriceHigh })).Items[0].Price);
    }

    private Task<PropertyPageDto> Search(PropertySearchInput search) => host.SendAs(CompanyA, new GetPropertiesQuery(search));

    [Fact]
    public async Task Owners_hold_shares_and_their_details_show_only_with_owner_permission()
    {
        var plot = await Add(Plot());
        var husband = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = "Vishal Gupta", Relation = RelationKind.SonOf, RelativeName = "Suresh Kumar Gupta", Phone = "98140 12345", Aadhaar = "2291 7264 8905", Pan = "abcde1234f",
        }));
        var wife = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Mira Gupta", Relation = RelationKind.WifeOf, RelativeName = "Vishal Gupta" }));
        Assert.Equal(("8905", "234F", "919814012345"), (husband.AadhaarLast4, husband.PanLast4, husband.Phone));

        var equal = await host.SendAs(CompanyA, new SetPropertyOwnersCommand(plot.Id,
            [new PropertyOwnerInput(husband.Id, null, HoldingKind.BuilderAllotment), new PropertyOwnerInput(wife.Id, null, HoldingKind.BuilderAllotment)]));
        Assert.Equal([50m, 50m], equal.Owners.Select(o => o.SharePercent));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SetPropertyOwnersCommand(plot.Id,
            [new PropertyOwnerInput(husband.Id, 60, HoldingKind.BuilderAllotment), new PropertyOwnerInput(wife.Id, 30, HoldingKind.BuilderAllotment)])));
        var thirds = SetPropertyOwnersHandler.Shares([new(husband.Id, null, HoldingKind.Other), new(wife.Id, null, HoldingKind.Other), new("x", null, HoldingKind.Other)]);
        Assert.Equal(100m, thirds.Sum());

        var agent = await host.SendAsMember(CompanyA, "agent-1", [EstatePermissions.PropertyView], new GetPropertyQuery(plot.Id));
        Assert.Equal(("Vishal Gupta", (string?)null), (agent.Owners[0].Name, agent.Owners[0].Phone));
        var dealer = await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id));
        Assert.Equal("919814012345", dealer.Owners[0].Phone);

        // The Aadhaar is kept sealed, never in clear.
        await host.WithAsync(CompanyA, async sp =>
        {
            var stored = await sp.GetRequiredService<IPartyRepository>().GetDocument(husband.Id).FirstAsync();
            Assert.DoesNotContain("229172648905", stored.AadhaarSealed);
        });
    }

    [Fact]
    public async Task People_go_to_the_contact_book_tagged_by_their_place_without_identity_numbers()
    {
        var plot = await Add(Plot());
        var owner = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = "Vishal Gupta", Phone = "98140 12345", Email = "vishal@example.test", Aadhaar = "1234 5678 9012", Pan = "abcde1234f", Address = "Sector 70",
        }));
        await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "No Phone" }));   // nothing to find them by
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(plot.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.BuilderAllotment)]));

        var sent = host.Contacts.Sent.ToList();
        Assert.Equal(2, sent.Count);
        Assert.Equal((CompanyA, "Vishal Gupta", "919814012345", "vishal@example.test", $"party:{owner.Id}"),
            (sent[0].CompanyId, sent[0].Name, sent[0].Phone, sent[0].Email, sent[0].ExternalRef));
        Assert.Equal(["Property party"], sent[0].Tags);
        Assert.Equal(["Property party", "Property owner"], sent[1].Tags);
        Assert.DoesNotContain(sent, e => System.Text.Json.JsonSerializer.Serialize(e).Contains("9012") || System.Text.Json.JsonSerializer.Serialize(e).Contains("Sector 70"));
    }

    [Fact]
    public async Task Without_the_master_key_identity_numbers_are_refused_but_people_are_saved()
    {
        await using var bare = new TestHost(mongo, encryptionKey: "x");
        await Assert.ThrowsAsync<ValidatorException>(() => bare.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "A", Aadhaar = "229172648905" })));
        Assert.Equal("A", (await bare.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "A" }))).Name);
    }

    [Fact]
    public async Task A_duplicate_is_found_by_unit_or_by_owner_phone_type_and_size()
    {
        var flat = await Add(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "Sector 65", Society = "Beverly Golf Avenue", UnitNumber = "918",
            SuperArea = new AreaInput(2072.05m, AreaUnit.SqFt),
        });
        var owner = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Gaurav", Phone = "9876500000" }));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.BuilderAllotment)]));

        var byUnit = await host.SendAs(CompanyA, new GetDuplicatesQuery(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "sector 65", Society = "Beverly Golf Avenue", UnitNumber = "918",
        }, null, null));
        Assert.Equal(flat.Code, Assert.Single(byUnit).Code);

        var byOwner = await host.SendAs(CompanyA, new GetDuplicatesQuery(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, Locality = "Sector 65", SuperArea = new AreaInput(2080, AreaUnit.SqFt),
        }, null, ["98765 00000"]));
        Assert.Equal("Same owner, type and size", Assert.Single(byOwner).Reason);
    }

    [Fact]
    public async Task The_dealers_own_fields_are_checked_and_searchable()
    {
        await host.SendAs(CompanyA, new SaveCustomFieldCommand(null, new CustomFieldInput("Phase", CustomFieldKind.Choice, ["Phase 1", "Phase 2"], null, true)));
        await host.SendAs(CompanyA, new SaveCustomFieldCommand(null, new CustomFieldInput("Temple facing", CustomFieldKind.YesNo, null, [PropertyType.Plot], true)));

        var plot = await Add(Plot());
        plot = await host.SendAs(CompanyA, new SavePropertyCommand(plot.Id, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Sector 70",
            Custom = [new CustomValueInput("phase", "phase 2"), new CustomValueInput("temple_facing", "yes"), new CustomValueInput("unknown", "x")],
        }));
        Assert.Equal([("Phase", "Phase 2"), ("Temple facing", "yes")], plot.Custom.Select(c => (c.Label, c.Value)).OrderBy(c => c.Label));
        await Assert.ThrowsAsync<ValidatorException>(() => Add(new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Sector 70", Custom = [new CustomValueInput("phase", "Phase 9")],
        }));
        Assert.Single((await Search(new PropertySearchInput { Custom = [new CustomValueInput("phase", "Phase 2")] })).Items);
    }

    [Fact]
    public async Task A_property_goes_stale_after_the_dealers_days_and_confirming_makes_it_fresh()
    {
        var plot = await Add(Plot());
        var rent = await Add(new PropertyInput { Purpose = PropertyPurpose.Rent, Type = PropertyType.Flat, Locality = "Sector 70", Rent = 30000 });
        await Backdate(plot.Id, 31);
        await Backdate(rent.Id, 16);

        var stale = await host.SendAs(CompanyA, new GetStalePropertiesQuery());
        Assert.Equal([plot.Id, rent.Id], stale.Select(p => p.Id));
        Assert.True((await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id))).Stale);
        Assert.Equal(2, (await Search(new PropertySearchInput { Stale = true })).TotalCount);

        var confirmed = await host.SendAs(CompanyA, new ConfirmPropertyCommand(plot.Id, new ConfirmPropertyInput(PropertyStatus.Available, 11500000m, ConfirmedVia.WhatsApp, null)));
        Assert.Equal((false, 11500000m, ConfirmedVia.WhatsApp), (confirmed.Stale, confirmed.Price, confirmed.ConfirmedVia));

        var sold = await host.SendAs(CompanyA, new ConfirmPropertyCommand(rent.Id, new ConfirmPropertyInput(PropertyStatus.Rented, null, ConfirmedVia.Call, null)));
        Assert.Equal(PropertyStatus.Rented, sold.Status);
        Assert.Empty(await host.SendAs(CompanyA, new GetStalePropertiesQuery()));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new ConfirmPropertyCommand(plot.Id, new ConfirmPropertyInput(PropertyStatus.OnHold, null, ConfirmedVia.Call, null))));
    }

    [Fact]
    public async Task An_ended_hold_makes_the_property_available_again()
    {
        var plot = await Add(Plot());
        await host.SendAs(CompanyA, new ConfirmPropertyCommand(plot.Id, new ConfirmPropertyInput(PropertyStatus.OnHold, null, ConfirmedVia.Call, "2020-01-01")));

        await host.RunAsJobAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<HoldRelease>().RunOnceAsync(CancellationToken.None)));
        var back = await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id));
        Assert.Equal((PropertyStatus.Available, (string?)null), (back.Status, back.HoldUntil));
    }

    [Fact]
    public async Task Localities_are_kept_as_typed_once_with_their_stock()
    {
        await Add(Plot("Sector 70"));
        await Add(Plot("sector  70"));
        await Add(Plot("Aerocity"));
        var localities = await host.SendAs(CompanyA, new GetLocalitiesQuery(null));
        Assert.Equal([("Aerocity", 1), ("Sector 70", 2)], localities.Select(l => (l.Name, l.Properties)));
    }

    private Task Backdate(string propertyId, int days) => host.WithAsync(CompanyA, async sp =>
    {
        var repo = sp.GetRequiredService<IPropertyRepository>();
        var doc = await repo.GetDocument(propertyId).FirstAsync();
        doc.LastConfirmedAt = DateTime.UtcNow.AddDays(-days);
        await repo.UpdateAsync(doc);
    });
}
