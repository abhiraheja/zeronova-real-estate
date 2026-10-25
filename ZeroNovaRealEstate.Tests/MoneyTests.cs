using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using ZeroNovaRealEstate.Application.Exceptions;
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

/// <summary>M4: each side's brokerage with GST, TDS, receipts and the invoice; tenancies with rent due, reminders, receipts, renewal and the end.</summary>
[Collection(MongoCollection.Name)]
public sealed class MoneyTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;
    private PartyDto owner = null!, buyer = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        owner = await Party("Harpreet Singh", "9814000001");
        buyer = await Party("Anjali Sharma", "9814000002");
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<PartyDto> Party(string name, string phone) =>
        host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = name, Relation = RelationKind.SonOf, RelativeName = "—", Phone = phone, Address = "Mohali" }));

    private async Task<PropertyDto> Property(PropertyPurpose purpose, decimal price)
    {
        var p = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = purpose, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 2, Price = price,
        }));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(p.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));
        return p;
    }

    private static string Today => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shift(int months, int days = 0) =>
        DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(months).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string TextOf(string base64)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(Convert.FromBase64String(base64)), false);
        return doc.MainDocumentPart!.Document.Body!.InnerText;
    }

    [Fact]
    public async Task Brokerage_per_side_with_gst_receipts_and_an_invoice()
    {
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali", null,
            "Sharma Properties", "9814099999", GstRegistered: true, Gstin: "03ABCDE1234F1Z5", DealerAddress: "SCO 12, Sector 70, Mohali")));
        var flat = await Property(PropertyPurpose.Sale, 5000000m);
        var deal = await host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput { PropertyId = flat.Id, BuyerIds = [buyer.Id], Price = 5000000m }));

        var fromBuyer = await host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Buyer }));
        Assert.Matches(@"^BR/\d{4}-\d{2}/0001$", fromBuyer.InvoiceNo);
        Assert.Equal((1m, 50000m, 18m, 9000m, 59000m), (fromBuyer.Rate, fromBuyer.Amount, fromBuyer.GstPercent, fromBuyer.Gst, fromBuyer.Total));
        Assert.Equal((buyer.Id, BrokerageStatus.Due, 59000m), (fromBuyer.PartyId, fromBuyer.Status, fromBuyer.Due));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveBrokerageCommand(null, new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Buyer })));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveBrokerageCommand(null,
            new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Basis = BrokerageBasis.MonthsRent, Rate = 1 })));

        // The seller's side: a business payer deducting 2% TDS (on the amount before GST).
        var fromSeller = await host.SendAs(CompanyA, new SaveBrokerageCommand(null,
            new BrokerageInput { DealId = deal.Id, Side = BrokerageSide.Seller, Basis = BrokerageBasis.Fixed, Rate = 40000m, TdsPercent = 2m }));
        Assert.Equal((owner.Id, 7200m, 800m, 46400m), (fromSeller.PartyId, fromSeller.Gst, fromSeller.Tds, fromSeller.Receivable));

        var part = await host.SendAs(CompanyA, new AddBrokerageReceiptCommand(fromBuyer.Id, new BrokerageReceiptInput(20000m, PaymentMode.Upi, "UTR111", null)));
        Assert.Equal((BrokerageStatus.PartlyReceived, 39000m), (part.Status, part.Due));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new AddBrokerageReceiptCommand(fromBuyer.Id, new BrokerageReceiptInput(40000m, PaymentMode.Upi, null, null))));
        var paid = await host.SendAs(CompanyA, new AddBrokerageReceiptCommand(fromBuyer.Id, new BrokerageReceiptInput(39000m, PaymentMode.Neft, null, null)));
        Assert.Equal(BrokerageStatus.Received, paid.Status);

        var summary = await host.SendAs(CompanyA, new GetBrokerageSummaryQuery(null, null));
        Assert.Equal((90000m, 59000m, 46400m, 1), (summary.Earned, summary.Received, summary.DueNow, summary.OpenInvoices));
        Assert.Single(await host.SendAs(CompanyA, new GetBrokeragesQuery(null, BrokerageStatus.Due)));
        Assert.Empty(await host.SendAs(CompanyB, new GetBrokeragesQuery(null, null)));

        var invoice = await host.SendAs(CompanyA, new BuildBrokerageInvoiceCommand(fromBuyer.Id));
        var text = TextOf(invoice.DocxBase64);
        Assert.Contains("TAX INVOICE", text);
        Assert.Contains("GSTIN: 03ABCDE1234F1Z5", text);
        Assert.Contains("CGST 9%: Rs. 4,500/-", text);
        Assert.Contains("Rupees Fifty Nine Thousand Only", text);
        Assert.Empty(invoice.Warnings);

        var off = await host.SendAs(CompanyA, new WriteOffBrokerageCommand(fromSeller.Id, true, "Seller went abroad"));
        Assert.Equal((BrokerageStatus.WrittenOff, 0m), (off.Status, off.Due));
    }

    [Fact]
    public async Task A_tenancy_rings_for_rent_takes_payments_renews_and_ends()
    {
        var flat = await Property(PropertyPurpose.Rent, 25000m);
        var start = Shift(-2)[..8] + "01";
        var tenancy = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, TenantIds = [buyer.Id], Rent = 25000m, Deposit = 50000m, StartDate = start, Months = 11, RentDueDay = 1, EscalationPercent = 5,
        }));
        Assert.Equal(["Harpreet Singh"], tenancy.LandlordNames);
        Assert.Equal(DateOnly.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(11).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), tenancy.EndDate);
        Assert.True(tenancy.RentDueNow);
        Assert.Contains("Namaste Anjali Sharma, the rent of ₹25,000", tenancy.RentReminder);
        Assert.Equal(PropertyStatus.Rented, (await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Status);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, TenantIds = [buyer.Id], Rent = 1000m, StartDate = start,
        })));

        host.Bells.Sent.Clear();
        await host.RunAsJobAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None)));
        await host.RunAsJobAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None)));
        Assert.Equal("ESTATE_RENT_DUE", Assert.Single(host.Bells.Sent).Type);

        var month = Today[..7];
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new RecordRentPaymentCommand(tenancy.Id, new RentPaymentInput("2001-01", 25000m, PaymentMode.Upi, null, null))));
        var part = await host.SendAs(CompanyA, new RecordRentPaymentCommand(tenancy.Id, new RentPaymentInput(month, 10000m, PaymentMode.Upi, "UTR1", null)));
        Assert.True(part.RentDueNow);
        var full = await host.SendAs(CompanyA, new RecordRentPaymentCommand(tenancy.Id, new RentPaymentInput(month, 15000m, PaymentMode.Cash, null, null)));
        Assert.False(full.RentDueNow);
        Assert.Single(await host.SendAs(CompanyA, new GetTenanciesQuery(TenancyStatus.Active, null)));
        Assert.Empty(await host.SendAs(CompanyA, new GetTenanciesQuery(null, null, RentDueOnly: true)));

        var receipt = await host.SendAs(CompanyA, new BuildRentReceiptCommand(tenancy.Id, month));
        var text = TextOf(receipt.DocxBase64);
        Assert.Contains("Received with thanks from Anjali Sharma the sum of Rs. 25,000/- (Rupees Twenty Five Thousand Only)", text);
        Assert.Contains("Landlord: Harpreet Singh", text);
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new BuildRentReceiptCommand(tenancy.Id, "2001-01")));

        // Another agent without PRNV sees none of it.
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "user-other", ["PRNM"], new GetTenancyQuery(tenancy.Id)));
        Assert.Empty(await host.SendAsMember(CompanyA, "user-other", ["PRNM"], new GetTenanciesQuery(null, null)));

        var renewed = await host.SendAs(CompanyA, new RenewTenancyCommand(tenancy.Id, 11, null));
        Assert.Equal((26250m, DateOnly.ParseExact(tenancy.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            (renewed.Rent, renewed.StartDate));

        var ended = await host.SendAs(CompanyA, new EndTenancyCommand(tenancy.Id, null, "Tenant moved to Pune"));
        Assert.Equal((TenancyStatus.Ended, Today), (ended.Status, ended.EndDate));
        Assert.Equal(PropertyStatus.Available, (await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Status);
    }

    [Fact]
    public async Task A_tenancy_ending_within_a_month_rings_once_for_renewal()
    {
        var flat = await Property(PropertyPurpose.Rent, 18000m);
        var tenancy = await host.SendAs(CompanyA, new SaveTenancyCommand(null, new TenancyInput
        {
            PropertyId = flat.Id, TenantIds = [buyer.Id], Rent = 18000m, StartDate = Shift(-11, 20), Months = 11, RentDueDay = 28,
        }));
        Assert.InRange(tenancy.DaysToEnd, 15, 25);
        Assert.Contains("ends on", tenancy.RenewalMessage);

        host.Bells.Sent.Clear();
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<RentReminders>().RunOnceAsync(CancellationToken.None));
        Assert.Single(host.Bells.Sent, b => b.Type == "ESTATE_TENANCY_ENDING");
    }
}
