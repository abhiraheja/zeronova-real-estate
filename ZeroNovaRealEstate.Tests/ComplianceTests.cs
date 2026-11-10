using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using ZeroNovaRealEstate.Application.Features.Agreements;
using ZeroNovaRealEstate.Application.Features.Brokerage;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Rentals;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Money;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// C5 (D37, D38): the property TDS (section by date, ₹50 lakh or more, every instalment, 20% without a PAN, s.195 for
/// a non-resident seller), brokerage TDS by payer, Haryana's brokerage cap, FEMA on farm land, GPA, ordered transfer
/// steps and the new kinds, police verification and Chandigarh's Rent Authority intimation, the stamp duty estimate, and
/// the commercial and registered lease templates.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class ComplianceTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;
    private int phones = 14000000;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private static string Today => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shift(int days) =>
        DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task<PartyDto> Party(string name, PartyGender? gender = null, PartyResidency? residency = null, string? pan = "ABCDE1234F") =>
        host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = name, Relation = gender == PartyGender.Female ? RelationKind.WifeOf : RelationKind.SonOf, RelativeName = "Ram Lal", Address = "Sector 70, Mohali",
            Phone = "98" + Interlocked.Increment(ref phones).ToString("00000000", CultureInfo.InvariantCulture), Pan = pan, Gender = gender, Residency = residency,
        }));

    private async Task<PropertyDto> Property(decimal price, string? state = "Punjab", PropertyType type = PropertyType.Flat, PropertyPurpose purpose = PropertyPurpose.Sale,
        bool rural = false, IReadOnlyList<(PartyDto Owner, HoldingKind Holding)>? owners = null)
    {
        var p = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = purpose, Type = type, City = "Mohali", State = state, Rural = rural, Locality = "Sector 70", Bhk = 3, Price = price, Rent = price,
            SuperArea = new AreaInput(1800m, AreaUnit.SqFt),
        }));
        if (owners is { Count: > 0 })
            await host.SendAs(CompanyA, new SetPropertyOwnersCommand(p.Id, [.. owners.Select(o => new PropertyOwnerInput(o.Owner.Id, null, o.Holding))]));
        return p;
    }

    private Task<DealDto> Deal(PropertyDto property, IReadOnlyList<string> buyers, decimal price, string? executionDate = "2026-05-19",
        IReadOnlyList<DealPaymentInput>? payments = null, IReadOnlyList<DealInstalmentInput>? schedule = null, TransferKind? kind = null) =>
        host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput
        {
            PropertyId = property.Id, BuyerIds = buyers, Price = price, ExecutionDate = executionDate, ExecutionPlace = "Mohali", TransferKind = kind,
            Payments = payments, Schedule = schedule, Witnesses = [new WitnessInput("Ramesh", "Zirakpur"), new WitnessInput("Suresh", "Kharar")],
        }));

    // ── Property TDS ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tds_is_due_at_fifty_lakh_exactly_on_every_payment_under_the_section_of_its_date()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var buyer = await Party("Anjali Sharma", PartyGender.Female);
        var flat = await Property(50_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]);

        var deal = await Deal(flat, [buyer.Id], 50_00_000m, executionDate: "2026-03-31",
            payments: [new DealPaymentInput(PaymentMode.Upi, 1_00_000m, "UTR-TOKEN", null, "2026-03-31", buyer.Id),
                new DealPaymentInput(PaymentMode.Rtgs, 4_00_000m, "UTR-AGR", null, "2026-04-01", buyer.Id)],
            schedule: [new DealInstalmentInput(PaymentMode.Rtgs, 45_00_000m, "2026-06-30")]);

        Assert.Equal(50_000m, deal.Tds);
        var tds = deal.TdsDetail!;
        Assert.True(tds.Applies);
        Assert.Equal((1m, 50_000m, "s.194-IA"), (tds.RatePercent!.Value, tds.OnFullPrice!.Value, tds.Section));   // agreement before 1 April 2026
        // Every instalment, the token included, each under the section of its own date.
        Assert.Equal([(1_000m, "s.194-IA"), (4_000m, "s.393(1) of the Income-tax Act 2025")], deal.Payments.Select(p => (p.Tds!.Value, p.TdsSection!)));
        Assert.Equal((45_000m, "s.393(1) of the Income-tax Act 2025"), (deal.Schedule[0].Tds!.Value, deal.Schedule[0].TdsSection!));
        Assert.Contains(deal.Warnings, w => w.StartsWith("1% TDS (Rs. 50,000/-) is due on the full price under s.194-IA"));

        // From 1 April 2026 the agreement's clause names the new section.
        var after = await host.SendAs(CompanyA, new SaveDealCommand(deal.Id, new DealInput
        {
            PropertyId = flat.Id, BuyerIds = [buyer.Id], Price = 50_00_000m, ExecutionDate = "2026-04-01", ExecutionPlace = "Mohali",
        }));
        Assert.Equal("s.393(1) of the Income-tax Act 2025", after.TdsDetail!.Section);
        var preview = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-a"));
        Assert.Contains("TDS at 1% (Rs. 50,000/-) on the full price under s.393(1) of the Income-tax Act 2025 from every payment", preview.Text);

        // One rupee less over the whole deal: no TDS.
        var below = await Deal(await Property(49_99_999m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [buyer.Id], 49_99_999m,
            payments: [new DealPaymentInput(PaymentMode.Upi, 1_00_000m, "UTR-B", null, "2026-05-19", buyer.Id)]);
        Assert.Null(below.Tds);
        Assert.False(below.TdsDetail!.Applies);
        Assert.Null(below.Payments[0].Tds);
        Assert.DoesNotContain(below.Warnings, w => w.Contains("TDS"));
    }

    [Fact]
    public async Task The_threshold_is_the_whole_deal_and_a_seller_without_pan_is_twenty_percent_on_their_share()
    {
        var withPan = await Party("Mira Gupta", PartyGender.Female);
        var noPan = await Party("Vishal Gupta", PartyGender.Male, pan: null);
        var b1 = await Party("Sunita Rani", PartyGender.Female);
        var b2 = await Party("Mohit Gupta", PartyGender.Male);
        var flat = await Property(60_00_000m, owners: [(withPan, HoldingKind.RegisteredSaleDeed), (noPan, HoldingKind.RegisteredSaleDeed)]);

        // Two buyers at ₹30 lakh each and two sellers: still ₹60 lakh over the whole deal.
        var deal = await Deal(flat, [b1.Id, b2.Id], 60_00_000m,
            payments: [new DealPaymentInput(PaymentMode.Rtgs, 2_00_000m, "UTR-1", null, "2026-05-19", b1.Id)]);

        Assert.Equal(10.5m, deal.TdsDetail!.RatePercent);   // 50% at 1% + 50% at 20%
        Assert.Equal(6_30_000m, deal.Tds);
        Assert.Equal(21_000m, deal.Payments[0].Tds);
        Assert.Equal([1m, 20m], deal.TdsDetail.Sellers.Select(s => s.RatePercent!.Value));
        Assert.Contains(deal.Warnings, w => w.StartsWith("TDS of 10.5%"));
        Assert.Contains(deal.Warnings, w => w == "Vishal Gupta has no PAN on record — TDS on their share is 20%, not 1%.");
    }

    [Fact]
    public async Task A_non_resident_seller_is_section_195_not_one_percent_whatever_the_price()
    {
        var nri = await Party("Gurpreet Kaur", PartyGender.Female, PartyResidency.Nri);
        var buyer = await Party("Anjali Sharma", PartyGender.Female);
        var flat = await Property(30_00_000m, owners: [(nri, HoldingKind.RegisteredSaleDeed)]);
        var deal = await Deal(flat, [buyer.Id], 30_00_000m,
            payments: [new DealPaymentInput(PaymentMode.Rtgs, 3_00_000m, "UTR-N", null, "2026-05-19", buyer.Id)]);

        var tds = deal.TdsDetail!;
        Assert.True(tds.Applies && tds.NonResidentSeller);
        Assert.Null(tds.RatePercent);
        Assert.Null(deal.Tds);
        Assert.Null(deal.Payments[0].Tds);
        Assert.Contains(PropertyTds.NonResidentText, tds.Note);
        Assert.Contains(deal.Warnings, w => w == $"Gurpreet Kaur is a non-resident seller (NRI) — {PropertyTds.NonResidentText}.");
        Assert.DoesNotContain(deal.Warnings, w => w.Contains("1% TDS"));

        var preview = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-a"));
        Assert.Contains("under section 195 of the Income Tax Act from the payments to Gurpreet Kaur, being non-resident, after obtaining a TAN, file Form 27Q", preview.Text);
        Assert.DoesNotContain("TDS at", preview.Text);
        Assert.Contains(preview.Warnings, w => w.Contains("s.195"));

        // Residency and gender are kept when an update leaves them out.
        var kept = await host.SendAs(CompanyA, new SavePartyCommand(nri.Id, new PartyInput { Name = "Gurpreet Kaur", Address = "Toronto" }));
        Assert.Equal((PartyResidency.Nri, PartyGender.Female), (kept.Residency!.Value, kept.Gender!.Value));
    }

    // ── Brokerage TDS and Haryana's cap ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Brokerage_tds_follows_the_payer_the_yearly_threshold_and_the_dealers_pan()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var company = await Party("Acme Retail Pvt Ltd");
        var flat = await Property(1_00_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]);
        var deal = await Deal(flat, [company.Id], 1_00_00_000m);

        // An individual pays by default: no TDS, with the note.
        var fromSeller = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Rate = 0.5m }));
        Assert.Equal((PayerKind.IndividualOrHuf, 0m, 0m), (fromSeller.PayerKind!.Value, fromSeller.TdsPercent, fromSeller.Tds));
        Assert.Equal(BrokerageTds.IndividualNote, fromSeller.TdsNote);
        Assert.Null(fromSeller.TdsSection);

        // A company under ₹20,000 in the year: none yet.
        var first = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput
        {
            DealId = deal.Id, Side = BrokerageSide.Buyer, PartyId = company.Id, Basis = BrokerageBasis.Fixed, Rate = 15_000m, PayerKind = PayerKind.CompanyOrFirm,
        }));
        Assert.Equal(0m, first.TdsPercent);
        Assert.Contains("over ₹20,000", first.TdsNote);

        // Its next brokerage takes the year over ₹20,000; the dealer has no PAN on record yet: 20%.
        var other = await Deal(await Property(80_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [company.Id], 80_00_000m);
        var second = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput
        {
            DealId = other.Id, Side = BrokerageSide.Buyer, PartyId = company.Id, Basis = BrokerageBasis.Fixed, Rate = 10_000m, PayerKind = PayerKind.CompanyOrFirm,
        }));
        Assert.Equal((20m, 2_000m), (second.TdsPercent, second.Tds));
        Assert.Contains("PAN is not in the settings", second.TdsNote);
        Assert.Equal(BrokerageTds.NewSection, second.TdsSection);   // invoiced after 1 April 2026

        // With the dealer's PAN in the settings: 2%, and the invoice names the section.
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali", null,
            "Sharma Properties", DealerPan: "abcde1234f")));
        var again = await host.SendAs(CompanyA, new SaveBrokerageCommand(second.Id, new BrokerageInput
        {
            DealId = other.Id, Side = BrokerageSide.Buyer, PartyId = company.Id, Basis = BrokerageBasis.Fixed, Rate = 10_000m,
        }));
        Assert.Equal((PayerKind.CompanyOrFirm, 2m, 200m), (again.PayerKind!.Value, again.TdsPercent, again.Tds));
        Assert.Equal("ABCDE1234F", (await host.SendAs(CompanyA, new GetEstateSettingsQuery())).DealerPan);
        var invoice = await host.SendAs(CompanyA, new BuildBrokerageInvoiceCommand(again.Id));
        Assert.Contains("Less TDS 2% (s.393(1) (Table Sl. 1(ii)) of the Income-tax Act 2025)", TextOf(invoice.DocxBase64));
    }

    [Fact]
    public async Task Haryana_warns_over_one_percent_in_all_or_half_a_percent_a_side()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var buyer = await Party("Anjali Sharma", PartyGender.Female);
        var panchkula = await Property(1_00_00_000m, state: "haryana", owners: [(seller, HoldingKind.RegisteredSaleDeed)]);
        Assert.Equal("Haryana", panchkula.State);
        var deal = await Deal(panchkula, [buyer.Id], 1_00_00_000m);

        var fromBuyer = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Buyer, Rate = 1m }));
        Assert.Equal(["The buyer's brokerage on " + deal.Code + " is 1% (Rs. 1,00,000/-) — Haryana allows 0.5% from each side."], fromBuyer.Warnings);
        var fromSeller = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Rate = 0.5m }));
        Assert.Equal([$"Haryana caps brokerage at 1% of the deal value, half from each side (Haryana property dealers rules 2009, rule 10) — the brokerage on {deal.Code} comes to 1.5% (Rs. 1,50,000/-)."],
            fromSeller.Warnings);

        // In the deal's checks (and so the agreement's) for those who see brokerage — never for those who do not.
        var checks = (await host.SendAs(CompanyA, new GetDealQuery(deal.Id))).Warnings;
        Assert.Contains(checks, w => w.StartsWith("Haryana caps brokerage at 1%"));
        Assert.Contains((await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-b"))).Warnings, w => w.StartsWith("Haryana caps brokerage"));
        var agent = await host.SendAsMember(CompanyA, "user-agent", ["PDLV"], new GetDealQuery(deal.Id));
        Assert.DoesNotContain(agent.Warnings, w => w.Contains("Haryana"));

        // Punjab has no cap.
        var mohali = await Deal(await Property(1_00_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [buyer.Id], 1_00_00_000m);
        var punjab = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = mohali.Id, Side = BrokerageSide.Buyer, Rate = 2m }));
        Assert.Empty(punjab.Warnings);
    }

    // ── FEMA, GPA ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_nri_or_oci_buyer_of_a_farmhouse_or_farm_land_is_warned_under_fema()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var nri = await Party("Jaspreet Gill", PartyGender.Male, PartyResidency.Nri);
        var oci = await Party("Simran Gill", PartyGender.Female, PartyResidency.Oci);
        var resident = await Party("Anjali Sharma", PartyGender.Female);
        var farmhouse = await Property(2_00_00_000m, type: PropertyType.Farmhouse, owners: [(seller, HoldingKind.RegisteredSaleDeed)]);

        var deal = await Deal(farmhouse, [nri.Id, oci.Id], 2_00_00_000m);
        Assert.Contains(deal.Warnings, w => w.StartsWith("Jaspreet Gill is an NRI buyer — under FEMA") && w.Contains("cannot buy a farmhouse"));
        Assert.Contains(deal.Warnings, w => w.StartsWith("Simran Gill is an OCI buyer — under FEMA"));
        Assert.Contains((await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-b"))).Warnings, w => w.Contains("FEMA"));

        var land = await Deal(await Property(90_00_000m, type: PropertyType.AgriculturalLand, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [nri.Id], 90_00_000m);
        Assert.Contains(land.Warnings, w => w.Contains("cannot buy agricultural land"));
        Assert.DoesNotContain((await Deal(farmhouse, [resident.Id], 2_00_00_000m)).Warnings, w => w.Contains("FEMA"));
        Assert.DoesNotContain((await Deal(await Property(90_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [nri.Id], 90_00_000m)).Warnings, w => w.Contains("FEMA"));
    }

    [Fact]
    public async Task An_owner_holding_by_gpa_is_warned_on_the_property_the_deal_and_the_agreement()
    {
        var holder = await Party("Rakesh Kumar", PartyGender.Male);
        var buyer = await Party("Anjali Sharma", PartyGender.Female);
        var plot = await Property(70_00_000m, type: PropertyType.Plot, owners: [(holder, HoldingKind.PowerOfAttorney)]);

        var property = await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id));
        Assert.Equal([$"Rakesh Kumar holds the property by GPA. {GpaCheck.Text}."], property.Warnings);
        var deal = await Deal(plot, [buyer.Id], 70_00_000m);
        Assert.Contains(deal.Warnings, w => w.Contains("GPA gives no title (Supreme Court, Suraj Lamp 2011) — check the chain"));
        Assert.Contains((await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-a"))).Warnings, w => w.Contains("Suraj Lamp"));

        var clean = await Property(70_00_000m, owners: [(holder, HoldingKind.RegisteredSaleDeed)]);
        Assert.Empty((await host.SendAs(CompanyA, new GetPropertyQuery(clean.Id))).Warnings);
    }

    // ── Transfer checklists ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_gmada_transfer_goes_in_order_and_the_new_kinds_have_their_own_steps()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var buyer = await Party("Anjali Sharma", PartyGender.Female);
        var plot = await Property(80_00_000m, type: PropertyType.Plot, owners: [(seller, HoldingKind.AuthorityAllotment)]);

        var deal = await Deal(plot, [buyer.Id], 80_00_000m, kind: TransferKind.AuthorityPlot);
        var ordered = deal.Checklist.Where(c => c.Step is not null).OrderBy(c => c.Step).ToList();
        Assert.Equal(["ndc", "sale_permission", "registry", "re_transfer", "mutation"], ordered.Select(c => c.Key));
        Assert.Equal(new int?[] { 15, 10, null, 5, 30 }, ordered.Select(c => c.TypicalDays));
        Assert.Contains(deal.Checklist, c => c is { Key: "tds", Step: null });

        var ticked = await host.SendAs(CompanyA, new UpdateDealChecklistCommand(deal.Id, new ChecklistUpdateInput("re_transfer", true, "2026-06-01", null)));
        Assert.Contains(ticked.Warnings, w => w.StartsWith("\"Re-transfer in GMADA / authority records\" is ticked before \"No Due Certificate (NDC)"));
        foreach (var key in new[] { "ndc", "sale_permission", "registry" })
            ticked = await host.SendAs(CompanyA, new UpdateDealChecklistCommand(deal.Id, new ChecklistUpdateInput(key, true, "2026-06-01", null)));
        Assert.DoesNotContain(ticked.Warnings, w => w.Contains("ticked before"));

        var loi = await Deal(plot, [buyer.Id], 80_00_000m, kind: TransferKind.GmadaLoi);
        Assert.Equal(["ndc", "loi_transfer", "loi_letter"], loi.Checklist.Where(c => c.Step is not null).OrderBy(c => c.Step).Select(c => c.Key));
        var fee = Assert.Single(loi.Checklist, c => c.Key == "loi_transfer");
        Assert.Equal(21, fee.TypicalDays);
        Assert.Contains("2.5% of the allotment price", fee.Hint);

        var leasehold = await Deal(plot, [buyer.Id], 80_00_000m, kind: TransferKind.ChandigarhLeasehold);
        Assert.Equal(["estate_permission", "unearned_increase", "registry", "mutation"], leasehold.Checklist.Where(c => c.Step is not null).Select(c => c.Key));
        Assert.Contains("one-third", Assert.Single(leasehold.Checklist, c => c.Key == "unearned_increase").Hint);
        var freehold = await Deal(plot, [buyer.Id], 80_00_000m, kind: TransferKind.ChandigarhFreehold);
        Assert.Equal(["conveyance", "registry", "mutation"], freehold.Checklist.Where(c => c.Step is not null).Select(c => c.Key));

        // An LOI completed: the buyer holds an authority allotment.
        await host.SendAs(CompanyA, new SetDealStageCommand(loi.Id, new DealStageInput(DealStage.Completed, null)));
        Assert.Equal(HoldingKind.AuthorityAllotment, Assert.Single((await host.SendAs(CompanyA, new GetPropertyQuery(plot.Id))).Owners).Holding);
    }

    // ── Rentals: police verification and Chandigarh's Rent Authority ────────────────────────────────────

    [Fact]
    public async Task A_started_tenancy_without_police_verification_rings_and_shows_until_it_is_recorded()
    {
        var landlord = await Party("Harpreet Singh", PartyGender.Male);
        var tenant = await Party("Anjali Sharma", PartyGender.Female);
        var flat = await Property(25_000m, purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)]);
        var tenancy = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, TenantIds = [tenant.Id], Rent = 25_000m, StartDate = Shift(-10), RentDueDay = 28,
        }));
        Assert.True(tenancy.Compliance.PoliceVerificationDue);
        Assert.Contains(tenancy.Compliance.Warnings, w => w.Contains("police verification is not recorded") && w.Contains("s.163 BNSS"));
        Assert.False(tenancy.Compliance.RentAuthorityRequired);   // Punjab

        host.Bells.Sent.Clear();
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        Assert.Single(host.Bells.Sent, b => b.Type == "ESTATE_POLICE_VERIFICATION");
        Assert.DoesNotContain(host.Bells.Sent, b => b.Type == "ESTATE_RENT_AUTHORITY_DUE");

        var done = await host.SendAs(CompanyA, new UpdateTenancyComplianceCommand(tenancy.Id, new TenancyComplianceInput
        {
            PoliceVerified = true, PoliceVerifiedOn = Shift(-2), PoliceStation = "Phase 8, Mohali", PoliceReference = "SAMPARK-4411",
            PoliceDocument = new PhotoInput("papers/police.pdf", "https://files.test/police.pdf"),
        }));
        Assert.False(done.Compliance.PoliceVerificationDue);
        Assert.Equal(("Phase 8, Mohali", "SAMPARK-4411", "https://files.test/police.pdf"),
            (done.Compliance.PoliceStation, done.Compliance.PoliceReference, done.Compliance.PoliceDocument!.Url));
        Assert.Empty(done.Compliance.Warnings);

        // A tenancy that starts later is not due yet.
        var later = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = (await Property(20_000m, purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)])).Id,
            TenantIds = [tenant.Id], Rent = 20_000m, StartDate = Shift(20),
        }));
        Assert.False(later.Compliance.PoliceVerificationDue);
    }

    [Fact]
    public async Task A_chandigarh_tenancy_owes_the_rent_authority_within_two_months_with_a_bell_and_the_uin()
    {
        var landlord = await Party("Harpreet Singh", PartyGender.Male);
        var tenant = await Party("Anjali Sharma", PartyGender.Female);
        var flat = await Property(40_000m, state: "UT Chandigarh", purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)]);
        Assert.Equal("Chandigarh", flat.State);
        var agreement = Shift(-50);
        var tenancy = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, TenantIds = [tenant.Id], Rent = 40_000m, StartDate = Shift(-45), AgreementDate = agreement, RentDueDay = 28,
        }));
        var due = DateOnly.ParseExact(agreement, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal((true, due, false), (tenancy.Compliance.RentAuthorityRequired, tenancy.Compliance.RentAuthorityDueDate, tenancy.Compliance.RentAuthorityOverdue));
        Assert.Contains(tenancy.Compliance.Warnings, w => w.StartsWith("Chandigarh: Rent Authority intimation due within 2 months of the agreement"));

        host.Bells.Sent.Clear();
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        var bell = Assert.Single(host.Bells.Sent, b => b.Type == "ESTATE_RENT_AUTHORITY_DUE");
        Assert.Contains("Rent Authority", bell.Notification!.Title);

        var done = await host.SendAs(CompanyA, new UpdateTenancyComplianceCommand(tenancy.Id, new TenancyComplianceInput
        {
            PoliceVerified = true, RentAuthorityDoneOn = Shift(-1), RentAuthorityUin = "CHD-UIN-2026-0042",
        }));
        Assert.Equal(("CHD-UIN-2026-0042", false), (done.Compliance.RentAuthorityUin, done.Compliance.RentAuthorityOverdue));
        Assert.Empty(done.Compliance.Warnings);

        // Not done and past the date: overdue.
        var old = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = (await Property(30_000m, state: "Chandigarh", purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)])).Id,
            TenantIds = [tenant.Id], Rent = 30_000m, StartDate = Shift(-80),
        }));
        Assert.True(old.Compliance.RentAuthorityOverdue);
        Assert.Contains(old.Compliance.Warnings, w => w.Contains("it is overdue"));
    }

    // ── Stamp duty estimate ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_stamp_duty_estimate_follows_the_state_the_area_and_the_buyers_genders()
    {
        var seller = await Party("Harpreet Singh", PartyGender.Male);
        var woman = await Party("Anjali Sharma", PartyGender.Female);
        var man = await Party("Mohit Gupta", PartyGender.Male);
        var unknown = await Party("Kiran Bedi");

        var punjab = await Property(1_00_00_000m, owners: [(seller, HoldingKind.RegisteredSaleDeed)]);
        var sole = (await Deal(punjab, [woman.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal(("Punjab", "Women", 5m, 5_00_000m, 1m, 1_00_000m, 6_00_000m),
            (sole.State, sole.Category, sole.DutyPercent!.Value, sole.Duty!.Value, sole.RegistrationPercent!.Value, sole.Registration!.Value, sole.Total!.Value));
        Assert.StartsWith("Estimate only", sole.Label);
        var joint = (await Deal(punjab, [woman.Id, man.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal(("Joint", 6m), (joint.Category, joint.DutyPercent!.Value));
        var guessed = (await Deal(punjab, [unknown.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal(("Men", 7m), (guessed.Category, guessed.DutyPercent!.Value));
        Assert.Contains(guessed.Notes, n => n == "Kiran Bedi's gender is not set — counted at the men's rate.");

        var rural = (await Deal(await Property(1_00_00_000m, state: "Haryana", rural: true, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [woman.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal((true, 3m), (rural.Rural, rural.DutyPercent!.Value));
        var urban = (await Deal(await Property(1_00_00_000m, state: "Haryana", owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [man.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal(7m, urban.DutyPercent);
        Assert.Contains(urban.Notes, n => n.StartsWith("Counted as urban"));

        // Chandigarh ships blank (unverified) until the dealer enters rates; the company's default state fills a property without one.
        var chandigarh = await Property(1_00_00_000m, state: "Chandigarh", owners: [(seller, HoldingKind.RegisteredSaleDeed)]);
        var blank = (await Deal(chandigarh, [man.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal(((decimal?)null, (decimal?)null), (blank.DutyPercent, blank.Total));
        Assert.Contains(blank.Notes, n => n.Contains("not set"));
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali", null,
            DefaultState: "chandigarh", StampDutyRates: [new StampDutyRateInput("Chandigarh", null, 6m, 4m, 5m, 1m, "Dealer's rates")])));
        var own = (await Deal(chandigarh, [man.Id], 1_00_00_000m)).StampDuty!;
        Assert.Equal((6m, 7_00_000m), (own.DutyPercent!.Value, own.Total!.Value));
        var noState = await Property(1_00_00_000m, state: null, owners: [(seller, HoldingKind.RegisteredSaleDeed)]);
        var byDefault = await Deal(noState, [woman.Id], 1_00_00_000m);
        Assert.Equal(("Chandigarh", 4m), (byDefault.State, byDefault.StampDuty!.DutyPercent!.Value));
        Assert.Single((await host.SendAs(CompanyA, new GetEstateSettingsQuery())).StampDutyRates);
        Assert.Null((await Deal(await Property(30_000m, purpose: PropertyPurpose.Rent, owners: [(seller, HoldingKind.RegisteredSaleDeed)]), [man.Id], 30_000m)).StampDuty);
    }

    // ── Lease templates ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_commercial_and_registered_leases_are_filled_from_the_tenancy_and_come_out_as_word()
    {
        var templates = await host.SendAs(CompanyA, new GetAgreementTemplatesQuery());
        Assert.Contains(templates, t => t is { Id: "builtin:commercial-lease", Kind: AgreementKind.CommercialLease, BuiltIn: true });
        Assert.Contains(templates, t => t is { Id: "builtin:registered-lease", Kind: AgreementKind.RegisteredLease });

        var landlord = await Party("Harpreet Singh", PartyGender.Male);
        var lessee = await Party("Acme Retail", PartyGender.Male);
        var showroom = await Property(1_20_000m, type: PropertyType.Showroom, purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)]);
        var deal = await Deal(showroom, [lessee.Id], 1_20_000m, executionDate: "2026-11-01");
        await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = showroom.Id, DealId = deal.Id, Rent = 1_20_000m, Deposit = 7_20_000m, Maintenance = 8_000m, StartDate = "2026-11-01", Months = 36,
            RentDueDay = 7, EscalationPercent = 5, LockInMonths = 12, NoticeMonths = 3, RentFreeDays = 45, PermittedUse = "a retail showroom for garments",
        }));

        var commercial = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:commercial-lease"));
        Assert.True(commercial.Editable);
        Assert.Contains("LEASE DEED (COMMERCIAL PREMISES)", commercial.Text);
        Assert.Contains("executed at Mohali on 1st November 2026", commercial.Text);
        Assert.Contains("Mr. Harpreet Singh S/O Ram Lal PAN ABCDE1234F R/O Sector 70, Mohali (hereinafter called the LESSOR", commercial.Text);
        Assert.Contains("for a period of 36 (thirty six) months commencing from 1st November 2026 and ending on 31st October 2029", commercial.Text);
        Assert.Contains("during the first 12 months (the lock-in period)", commercial.Text);
        Assert.Contains("The monthly rent shall be Rs. 1,20,000/- (Rupees One Lakh Twenty Thousand Only)", commercial.Text);
        Assert.Contains("on or before the 7th day of each month", commercial.Text);
        Assert.Contains("increase by 5% every twelve months", commercial.Text);
        Assert.Contains("rent-free fit-out period of 45 days", commercial.Text);
        Assert.Contains("Rs. 7,20,000/- (Rupees Seven Lakh Twenty Thousand Only) with the LESSOR as an interest-free security deposit", commercial.Text);
        Assert.Contains("maintenance charges of Rs. 8,000/- a month", commercial.Text);
        Assert.Contains("only for a retail showroom for garments", commercial.Text);
        Assert.Contains("giving 3 month(s)' notice", commercial.Text);
        Assert.Contains("more than eleven months, it shall be stamped and registered with the Sub-Registrar", commercial.Text);
        Assert.Contains("WITNESS 2: Suresh, Kharar", commercial.Text);
        Assert.DoesNotContain(AgreementEngine.Blank, commercial.Text);
        Assert.DoesNotContain("{{", commercial.Text);

        var registered = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:registered-lease"));
        Assert.StartsWith("Note: a lease over 11 months must be registered with the Sub-Registrar and stamp duty paid on it", registered.Text);
        Assert.Contains("LEASE DEED (RESIDENTIAL)", registered.Text);
        Assert.Contains("for a period of 36 (thirty six) months", registered.Text);
        Assert.Contains("Neither party shall end this lease within the first 12 months.", registered.Text);
        Assert.Contains("Electricity, water, maintenance (Rs. 8,000/- a month) and other charges", registered.Text);
        Assert.DoesNotContain(AgreementEngine.Blank, registered.Text);
        Assert.DoesNotContain("{{", registered.Text);

        var file = await host.SendAs(CompanyA, new BuildAgreementCommand(deal.Id, "builtin:commercial-lease", null));
        Assert.EndsWith("Commercial_lease.docx", file.FileName);
        Assert.Contains("rent-free fit-out period of 45 days", TextOf(file.DocxBase64));

        // An 11-month tenancy on the registered lease, and a lease with no tenancy, are warned.
        var flat = await Property(30_000m, purpose: PropertyPurpose.Rent, owners: [(landlord, HoldingKind.RegisteredSaleDeed)]);
        var shortDeal = await Deal(flat, [lessee.Id], 30_000m);
        Assert.Contains((await host.SendAs(CompanyA, new PreviewAgreementQuery(shortDeal.Id, "builtin:registered-lease"))).Warnings, w => w.StartsWith("No tenancy is saved"));
        await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, DealId = shortDeal.Id, Rent = 30_000m, StartDate = "2026-11-01", Months = 11,
        }));
        Assert.Contains((await host.SendAs(CompanyA, new PreviewAgreementQuery(shortDeal.Id, "builtin:registered-lease"))).Warnings,
            w => w.StartsWith("The tenancy is for 11 months"));
    }

    private static string TextOf(string base64)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(Convert.FromBase64String(base64)), false);
        return doc.MainDocumentPart!.Document.Body!.InnerText;
    }
}
