using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Agreements;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// M3: a deal with several sellers and buyers, the checks the owner's samples needed, completing it (owners move, the
/// history grows, the requirement closes), earlier owners from the papers, and agreements in Word — ready and the dealer's own.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class DealTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;
    private PropertyDto flat = null!;
    private PartyDto mira = null!, vishal = null!, sunita = null!, mohit = null!;
    private RequirementDto requirement = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        flat = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 65", Society = "Beverly Golf Avenue", Block = "Tower 3",
            UnitNumber = "411", Floor = 4, Bhk = 3, SuperArea = new AreaInput(2072.05m, AreaUnit.SqFt), CarpetArea = new AreaInput(1203.11m, AreaUnit.SqFt), Price = 21600000m,
        }));
        mira = await Party("Mira Gupta", RelationKind.WifeOf, "Vishal Gupta", "359937523954", "AMNPG7188C");
        vishal = await Party("Vishal Gupta", RelationKind.SonOf, "Suresh Kumar Gupta", "229172648905", "APAPG8229E");
        sunita = await Party("Sunita Rani", RelationKind.WifeOf, "Ashok Kumar", "365475204839", null);
        mohit = await Party("Mohit Gupta", RelationKind.SonOf, "Vijay Pal Gupta", "111122223333", "ABCDE1234F");
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(flat.Id,
            [new PropertyOwnerInput(mira.Id, null, HoldingKind.BuilderAllotment), new PropertyOwnerInput(vishal.Id, null, HoldingKind.BuilderAllotment)]));
        requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-7", LeadName = "Sunita", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Flat], BudgetMax = 22000000m,
        }));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<PartyDto> Party(string name, RelationKind relation, string relative, string aadhaar, string? pan) =>
        host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput
        {
            Name = name, Relation = relation, RelativeName = relative, Aadhaar = aadhaar, Pan = pan, Address = "Sector 65, Mohali", Phone = "98" + aadhaar[..8],
        }));

    private DealInput Deal(IReadOnlyList<string>? sellers = null, IReadOnlyList<DealPaymentInput>? payments = null) => new()
    {
        PropertyId = flat.Id, SellerIds = sellers, BuyerIds = [sunita.Id, mohit.Id], RequirementId = requirement.Id, LeadId = "lead-7", Price = 21600000m,
        EarnestPercent = 10, TransferKind = TransferKind.BuilderAllotment, ExecutionPlace = "Mohali", ExecutionDate = "2026-05-19", FinalDate = "2026-12-30",
        Payments = payments ??
        [
            new DealPaymentInput(PaymentMode.Imps, 100000m, "T2M131026", null, "2026-05-11", sunita.Id),
            new DealPaymentInput(PaymentMode.Rtgs, 2000000m, "KKBKR52026051900763947", "Kotak", "2026-05-19", mohit.Id),
        ],
        Schedule = [new DealInstalmentInput(PaymentMode.Rtgs, 19500000m, "2026-12-30")],
        Witnesses = [new WitnessInput("Ramesh", "Zirakpur"), new WitnessInput("Suresh", "Kharar")],
        TitleNote = "Allotment Letter dated 26.09.2024 issued by M/s MB Infrabuild Pvt. Ltd.",
        Inclusions = ["Two covered car parking spaces", "Club membership, power backup, IFMS and transfer charges paid by the sellers"],
    };

    [Fact]
    public async Task Sellers_default_to_the_owners_and_the_money_and_checklist_come_out()
    {
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, Deal()));

        Assert.Equal(("D-0001", 2), (deal.Code, deal.Sellers.Count));
        Assert.Equal(["Mira Gupta", "Vishal Gupta"], deal.Sellers.Select(s => s.Name));
        Assert.Equal((2100000m, 19500000m, 2160000m, 216000m), (deal.Paid, deal.Balance, deal.EarnestAmount, deal.Tds));
        Assert.Equal("Rupees Two Crore Sixteen Lakh Only", deal.PriceInWords);
        Assert.Equal("builder_no_dues", deal.Checklist[0].Key);
        Assert.Contains(deal.Warnings, w => w.StartsWith("1% TDS"));
        Assert.DoesNotContain(deal.Warnings, w => w.Contains("not a seller"));
    }

    [Fact]
    public async Task The_samples_slips_are_caught_before_printing()
    {
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, Deal(sellers: [vishal.Id], payments:
        [
            new DealPaymentInput(PaymentMode.Rtgs, 2000000m, "UTR-SAME", null, "2026-05-19", mohit.Id),
            new DealPaymentInput(PaymentMode.Rtgs, 250000m, "UTR-SAME", null, "2026-05-20", mohit.Id),
            new DealPaymentInput(PaymentMode.Cash, 4300000m, null, null, "2026-05-19", sunita.Id),
        ])));

        Assert.Contains(deal.Warnings, w => w.StartsWith("Mira Gupta owns 50% of the property but is not a seller"));
        Assert.Contains(deal.Warnings, w => w.Contains("The same reference UTR-SAME is on 2 payments"));
        Assert.Contains(deal.Warnings, w => w.Contains("dated 20.05.2026, after the agreement's date 19.05.2026"));
        Assert.Contains(deal.Warnings, w => w.Contains("in cash") && w.Contains("269SS"));
        Assert.Contains(deal.Warnings, w => w.Contains("add up to") && w.Contains("not the price"));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveDealCommand(null, Deal(sellers: [sunita.Id]))));   // seller and buyer
    }

    [Fact]
    public async Task Completing_the_sale_moves_the_owners_grows_the_history_and_closes_the_requirement()
    {
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, Deal()));
        await host.SendAs(CompanyA, new UpdateDealChecklistCommand(deal.Id, new ChecklistUpdateInput("builder_noc", true, "2026-06-01", "NOC no. 554")));
        await host.SendAs(CompanyA, new AddOwnershipRecordCommand(flat.Id, new OwnershipRecordInput
        {
            FromNames = ["M/s MB Infrabuild Pvt. Ltd."], ToNames = ["Mira Gupta", "Vishal Gupta"], Kind = TransferKind.BuilderAllotment, Date = "2024-09-26",
            Note = "Original allotment",
        }));

        var done = await host.SendAs(CompanyA, new SetDealStageCommand(deal.Id, new DealStageInput(DealStage.Completed, null)));
        Assert.NotNull(done.CompletedAt);

        var property = await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id));
        Assert.Equal(PropertyStatus.Sold, property.Status);
        Assert.Equal(["Sunita Rani", "Mohit Gupta"], property.Owners.Select(o => o.Name));
        Assert.Equal([50m, 50m], property.Owners.Select(o => o.SharePercent));
        Assert.Equal(2, property.History.Count);
        Assert.Equal(["M/s MB Infrabuild Pvt. Ltd."], property.History[0].From);
        Assert.Equal("Original allotment", property.History[0].Note);
        Assert.Equal(["Mira Gupta", "Vishal Gupta"], property.History[1].From);
        Assert.Equal(deal.Id, property.History[1].DealId);

        var closed = await host.SendAs(CompanyA, new GetRequirementQuery(requirement.Id));
        Assert.Equal((RequirementStatus.Closed, $"Bought {flat.Code} through us"), (closed.Status, closed.CloseReason));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveDealCommand(deal.Id, Deal())));
        Assert.True(Assert.Single((await host.SendAs(CompanyA, new GetDealQuery(deal.Id))).Checklist, c => c.Key == "builder_noc").Done);
    }

    [Fact]
    public async Task A_ready_agreement_reads_right_and_comes_out_as_editable_word()
    {
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, Deal()));
        var preview = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, "builtin:format-a"));

        Assert.True(preview.Editable);
        Assert.Contains("executed at Mohali on this 19th May 2026", preview.Text);
        Assert.Contains("Mrs. Mira Gupta W/O Vishal Gupta having Aadhaar No. 3599 3752 3954 PAN AMNPG7188C R/O Sector 65, Mohali and Mr. Vishal Gupta S/O Suresh Kumar Gupta", preview.Text);
        Assert.Contains("for their bonafide needs", preview.Text);   // two sellers: they/their
        Assert.Contains("Rs. 2,16,00,000/- (Rupees Two Crore Sixteen Lakh Only)", preview.Text);
        Assert.Contains("RTGS of Rs. 20,00,000/- (Rupees Twenty Lakh Only) having UTR No KKBKR52026051900763947 Bank: Kotak Dt. 19.05.2026 from Mohit Gupta", preview.Text);
        Assert.Contains("balance sum of Rs. 1,95,00,000/-", preview.Text);
        Assert.Contains("before or by 30th December 2026", preview.Text);
        Assert.Contains("the 3 BHK Flat No. 411, 4th Floor, Tower 3, Beverly Golf Avenue, Sector 65, Mohali, comprising a total area of 2,072.05 sq ft", preview.Text);
        Assert.Contains("TDS at 1% (Rs. 2,16,000/-)", preview.Text);
        Assert.Contains("1. Two covered car parking spaces", preview.Text);
        Assert.Contains("WITNESS 2: Suresh, Kharar", preview.Text);

        var edited = preview.Text.Replace("Mohali on this", "Mohali (Punjab) on this");
        var file = await host.SendAs(CompanyA, new BuildAgreementCommand(deal.Id, "builtin:format-a", edited));
        Assert.EndsWith(".docx", file.FileName);
        using var stream = new MemoryStream(Convert.FromBase64String(file.DocxBase64));
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = string.Join('\n', doc.MainDocumentPart!.Document.Descendants<Paragraph>().Select(p => p.InnerText));
        Assert.Contains("Mohali (Punjab) on this", text);
        Assert.Equal(20160u, doc.MainDocumentPart.Document.Descendants<PageSize>().Single().Height!.Value);   // legal paper
    }

    [Fact]
    public async Task The_dealers_own_word_template_is_filled_in_place_with_its_loops()
    {
        var template = AgreementEngine.ToDocx("# MY FORMAT\nSeller: {{sellers.names}} and buyer {{buyers.names}}\n{{#payments}}{{.n}}) {{.figures}} on {{.date}}{{/payments}}\nTotal {{price.words}}");
        var saved = await host.SendAs(CompanyA, new SaveAgreementTemplateCommand(null, new AgreementTemplateInput("Sharma format", AgreementKind.AgreementToSell,
            Convert.ToBase64String(template))));
        Assert.Contains("#payments", saved.Placeholders);
        Assert.Contains("sellers.names", saved.Placeholders);
        Assert.Contains(await host.SendAs(CompanyA, new GetAgreementTemplatesQuery()), t => t.Id == saved.Id && !t.BuiltIn);

        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, Deal()));
        var preview = await host.SendAs(CompanyA, new PreviewAgreementQuery(deal.Id, saved.Id));
        Assert.False(preview.Editable);
        Assert.Contains("Seller: Mrs. Mira Gupta and Mr. Vishal Gupta and buyer Mrs. Sunita Rani and Mr. Mohit Gupta", preview.Text);
        Assert.Contains("1) Rs. 1,00,000/- on 11.05.2026", preview.Text);
        Assert.Contains("2) Rs. 20,00,000/- on 19.05.2026", preview.Text);
        Assert.Contains("Total Rupees Two Crore Sixteen Lakh Only", preview.Text);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveAgreementTemplateCommand(null,
            new AgreementTemplateInput("bad", AgreementKind.Other, Convert.ToBase64String("not a docx"u8.ToArray())))));
    }

    [Theory]
    [InlineData(21721000, "Two Crore Seventeen Lakh Twenty One Thousand")]
    [InlineData(99000, "Ninety Nine Thousand")]
    [InlineData(17700000, "One Crore Seventy Seven Lakh")]
    [InlineData(2500000000, "Two Hundred Fifty Crore")]
    [InlineData(1005, "One Thousand Five")]
    public void Rupees_in_words_the_indian_way(long amount, string words) => Assert.Equal(words, IndianWords.Words(amount));
}
