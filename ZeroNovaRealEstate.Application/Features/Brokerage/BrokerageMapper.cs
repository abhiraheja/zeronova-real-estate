using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Money;

namespace ZeroNovaRealEstate.Application.Features.Brokerage;

/// <summary>Brokerages with their deal, property and party named, and the money worked out.</summary>
public sealed class BrokerageMapper(IDealRepository deals, IPropertyRepository properties, IPartyRepository parties)
{
    public async Task<BrokerageDto> OneAsync(BrokerageDocument row, CancellationToken ct) => (await ManyAsync([row], ct))[0];

    public async Task<IReadOnlyList<BrokerageDto>> ManyAsync(IReadOnlyList<BrokerageDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var dealIds = rows.Select(r => r.DealId).Distinct().ToList();
        var dealRows = (await deals.GetDocuments(d => dealIds.Contains(d.Id), true).ToListAsync(ct)).ToDictionary(d => d.Id);
        var propertyIds = dealRows.Values.Select(d => d.PropertyId).Distinct().ToList();
        var props = (await properties.GetDocuments(p => propertyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var partyIds = rows.Select(r => r.PartyId).OfType<string>().Distinct().ToList();
        var names = (await parties.GetDocuments(p => partyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);

        return [.. rows.Select(r =>
        {
            var deal = dealRows.GetValueOrDefault(r.DealId);
            var property = deal is null ? null : props.GetValueOrDefault(deal.PropertyId);
            var gst = BrokerageMath.Gst(r);
            var tds = BrokerageMath.Tds(r);
            var receivable = BrokerageMath.Receivable(r);
            var received = r.Receipts.Sum(x => x.Amount);
            var due = r.WrittenOff ? 0m : Math.Max(0m, receivable - received);
            var status = r.WrittenOff ? BrokerageStatus.WrittenOff : due == 0 ? BrokerageStatus.Received : received > 0 ? BrokerageStatus.PartlyReceived : BrokerageStatus.Due;
            return new BrokerageDto(r.Id, r.InvoiceNo, r.DealId, deal?.Code ?? "—", property?.Code ?? "—", property?.Title ?? "—", r.Side, r.PartyId,
                r.PartyId is { } pid ? names.GetValueOrDefault(pid) : null, r.Basis, r.Rate, r.Amount, r.GstPercent, gst, r.Amount + gst, r.TdsPercent, tds,
                receivable, [.. r.Receipts.Select(x => new BrokerageReceiptDto(x.Amount, x.Mode, x.Reference, x.Date))], received, due, status, r.Notes,
                new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)));
        })];
    }
}

/// <summary>The brokerage bill's text (the <c># heading</c> line language of the agreements), and what is missing from it.</summary>
public sealed class BrokerageBook(IDealRepository deals, IPropertyRepository properties, IPartyRepository parties, IEstateSettingsRepository settings)
{
    public async Task<(string Text, IReadOnlyList<string> Warnings)> InvoiceAsync(BrokerageDocument row, CancellationToken ct)
    {
        var deal = await deals.GetDocument(row.DealId, true).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Deal not found.");
        var property = await properties.GetDocument(deal.PropertyId, true).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var party = row.PartyId is { } pid ? await parties.GetDocument(pid, true).FirstOrDefaultAsync(ct) : null;
        var s = await EstateSettings.LoadAsync(settings, ct);
        var warnings = new List<string>();
        if (s.DealerName is null)
            warnings.Add("Your firm's name is not in the settings.");
        if (row.GstPercent > 0 && s.Gstin is null)
            warnings.Add("GST is charged but your GSTIN is not in the settings.");
        if (party is null)
            warnings.Add("Who pays this brokerage is not set.");

        var gst = BrokerageMath.Gst(row);
        var total = row.Amount + gst;
        var basis = row.Basis switch
        {
            BrokerageBasis.Percent => $"{row.Rate:0.##}% of {IndianWords.Figures(deal.Price)}",
            BrokerageBasis.MonthsRent => $"{row.Rate:0.##} month(s) rent of {IndianWords.Figures(deal.Price)}",
            _ => "as agreed",
        };
        var lines = new List<string>
        {
            $"# {(row.GstPercent > 0 ? "TAX INVOICE" : "BILL")} — BROKERAGE",
            s.DealerName ?? AgreementBlank,
        };
        if (s.DealerAddress is not null)
            lines.Add(s.DealerAddress);
        if (s.Gstin is not null)
            lines.Add($"GSTIN: {s.Gstin}");
        if (s.ReraNumber is not null)
            lines.Add($"RERA agent registration: {s.ReraNumber}");
        if (s.DealerPhone is not null)
            lines.Add($"Phone: +{s.DealerPhone}");
        lines.Add(string.Empty);
        lines.Add($"Invoice no.: {row.InvoiceNo}        Date: {IndianWords.ShortDate(DateOnly.FromDateTime(row.CreatedAt.AddMinutes(330)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))}");
        lines.Add($"Billed to: {party?.Name ?? AgreementBlank}{(party?.Address is { } a ? ", " + a : string.Empty)}");
        lines.Add(string.Empty);
        lines.Add($"Brokerage for the {(deal.Kind == Models.Properties.PropertyPurpose.Rent ? "renting" : "sale")} of {property.Title} ({property.Code}, deal {deal.Code}), "
            + $"as the {row.Side.ToString().ToLowerInvariant()}'s broker: {basis}. SAC 997221.");
        lines.Add($"Brokerage: {IndianWords.Figures(row.Amount)}");
        if (row.GstPercent > 0)
        {
            var half = row.GstPercent / 2m;
            lines.Add($"CGST {half:0.##}%: {IndianWords.Figures(Math.Round(gst / 2m, 0))}    SGST {half:0.##}%: {IndianWords.Figures(gst - Math.Round(gst / 2m, 0))}");
        }
        lines.Add($"Total: {IndianWords.Figures(total)} ({IndianWords.Rupees(total)})");
        if (row.TdsPercent > 0)
            lines.Add($"Less TDS {row.TdsPercent:0.##}% (section 194H): {IndianWords.Figures(BrokerageMath.Tds(row))} — payable {IndianWords.Figures(BrokerageMath.Receivable(row))}");
        foreach (var r in row.Receipts)
            lines.Add($"Received {IndianWords.Figures(r.Amount)} by {r.Mode}{(r.Reference is { } rf ? " " + rf : string.Empty)} on {IndianWords.ShortDate(r.Date)}");
        lines.Add(string.Empty);
        lines.Add($"For {s.DealerName ?? AgreementBlank}");
        lines.Add(string.Empty);
        lines.Add("Authorised signatory");
        return (string.Join('\n', lines), warnings);
    }

    private const string AgreementBlank = Agreements.AgreementEngine.Blank;
}
