using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Deals;

/// <summary>Deals as members see them: parties named (Aadhaar and PAN as their last four), money totalled, and the checks before an agreement.</summary>
public sealed class DealMapper(IPropertyRepository properties, IPartyRepository parties)
{
    public async Task<DealDto> OneAsync(DealDocument deal, CancellationToken ct) => (await ManyAsync([deal], ct))[0];

    public async Task<IReadOnlyList<DealDto>> ManyAsync(IReadOnlyList<DealDocument> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var propertyIds = rows.Select(d => d.PropertyId).Distinct().ToList();
        var props = (await properties.GetDocuments(p => propertyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var partyIds = rows.SelectMany(d => d.SellerIds.Concat(d.BuyerIds).Concat(d.Payments.Select(p => p.PayerPartyId).OfType<string>()))
            .Concat(props.Values.SelectMany(p => p.Owners.Select(o => o.PartyId))).Distinct().ToList();
        var people = (await parties.GetDocuments(p => partyIds.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);

        return [.. rows.Where(d => props.ContainsKey(d.PropertyId)).Select(d =>
        {
            var p = props[d.PropertyId];
            var paid = d.Payments.Sum(x => x.Amount);
            decimal? tds = d.Kind == PropertyPurpose.Sale && d.Price >= 50_00_000m ? Math.Round(d.Price / 100m, 0) : null;
            return new DealDto(d.Id, d.Code, p.Id, p.Code, p.Title, d.Kind, d.Stage,
                [.. d.SellerIds.Select(id => Party(people, id))], [.. d.BuyerIds.Select(id => Party(people, id))],
                d.RequirementId, d.LeadId, d.AgentId, d.Price, IndianWords.Rupees(d.Price), d.EarnestPercent,
                d.EarnestPercent is { } pct ? Math.Round(d.Price * pct / 100m, 0) : null,
                [.. d.Payments.Select(x => new DealPaymentDto(x.Mode, x.Amount, IndianWords.Rupees(x.Amount), x.Reference, x.Bank, x.Date, x.PayerPartyId,
                    x.PayerPartyId is { } payer ? people.GetValueOrDefault(payer)?.Name : null))],
                paid,
                [.. d.Schedule.Select(x => new DealInstalmentDto(x.Mode, x.Amount, IndianWords.Rupees(x.Amount), x.DueDate))],
                d.Price - paid, d.FinalDate, d.TransferKind,
                [.. d.Checklist.Select(c => new ChecklistItemDto(c.Key, c.Label, c.Done, c.Date, c.Note))],
                d.ExecutionPlace, d.ExecutionDate, [.. d.Witnesses.Select(w => new WitnessDto(w.Name, w.Address))], d.PropertyDescription, d.TitleNote,
                d.Inclusions, d.Terms, d.Notes, tds, DealChecks.Warnings(d, p, people),
                d.CompletedAt is { } c ? new DateTimeOffset(DateTime.SpecifyKind(c, DateTimeKind.Utc)) : null, d.CancelReason, d.CreatedBy?.ChangedDate);
        })];
    }

    private static DealPartyDto Party(IReadOnlyDictionary<string, PartyDocument> people, string id) =>
        people.GetValueOrDefault(id) is { } p
            ? new DealPartyDto(p.Id, p.Name, DealChecks.RelationLabel(p.Relation), p.RelativeName, p.AadhaarLast4, p.PanLast4, p.Address)
            : new DealPartyDto(id, "—", null, null, null, null, null);
}

/// <summary>
/// What to fix before an agreement is printed (from the owner's samples, design 10.9): every co-owner signs, a GPA is
/// checked by a lawyer, the money adds up, dates agree, no UTR is used twice, nothing is left blank, TDS and cash.
/// </summary>
public static class DealChecks
{
    public static IReadOnlyList<string> Warnings(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people)
    {
        var w = new List<string>();
        string Name(string id) => people.GetValueOrDefault(id)?.Name ?? "Someone";

        foreach (var owner in p.Owners.Where(o => !d.SellerIds.Contains(o.PartyId)))
            w.Add($"{Name(owner.PartyId)} owns {owner.SharePercent:0.##}% of the property but is not a seller — every co-owner must sign.");
        if (p.Owners.Any(o => o.Holding == HoldingKind.PowerOfAttorney && d.SellerIds.Contains(o.PartyId)))
            w.Add("A seller holds the property by power of attorney (GPA) — get a lawyer to check it before signing.");
        if (d.SellerIds.Count == 0)
            w.Add("No seller yet. Add the property's owners or the sellers.");
        if (d.BuyerIds.Count == 0)
            w.Add("No buyer yet.");

        var paid = d.Payments.Sum(x => x.Amount);
        var scheduled = d.Schedule.Sum(x => x.Amount);
        if (paid > d.Price)
            w.Add($"Payments made ({IndianWords.Figures(paid)}) are more than the price ({IndianWords.Figures(d.Price)}).");
        else if (d.Schedule.Count > 0 && paid + scheduled != d.Price)
            w.Add($"Payments made ({IndianWords.Figures(paid)}) and the balance schedule ({IndianWords.Figures(scheduled)}) add up to "
                + $"{IndianWords.Figures(paid + scheduled)}, not the price {IndianWords.Figures(d.Price)}.");
        if (d.EarnestPercent is { } pct && d.Stage is DealStage.Agreement && paid < Math.Round(d.Price * pct / 100m, 0))
            w.Add($"The earnest money is {pct:0.##}% ({IndianWords.Figures(Math.Round(d.Price * pct / 100m, 0))}), but {IndianWords.Figures(paid)} is paid so far.");

        if (d.ExecutionDate is { } exec)
            foreach (var late in d.Payments.Where(x => x.Date is { } dt && string.CompareOrdinal(dt, exec) > 0))
                w.Add($"A payment of {IndianWords.Figures(late.Amount)} is dated {IndianWords.ShortDate(late.Date)}, after the agreement's date {IndianWords.ShortDate(exec)}.");
        foreach (var dup in d.Payments.Where(x => x.Reference is not null).GroupBy(x => x.Reference!).Where(g => g.Count() > 1))
            w.Add($"The same reference {dup.Key} is on {dup.Count()} payments — check the UTR or cheque numbers.");

        var cash = d.Payments.Where(x => x.Mode == PaymentMode.Cash).Sum(x => x.Amount) + d.Schedule.Where(x => x.Mode == PaymentMode.Cash).Sum(x => x.Amount);
        if (cash >= 20_000m)
            w.Add($"{IndianWords.Figures(cash)} in cash — the Income Tax Act limits cash in property deals (sections 269SS and 269ST). Check with your CA.");
        if (d.Kind == PropertyPurpose.Sale && d.Price >= 50_00_000m)
        {
            w.Add($"1% TDS ({IndianWords.Figures(Math.Round(d.Price / 100m, 0))}) is due on the full price (section 194-IA) — the buyer deducts and deposits it.");
            foreach (var seller in d.SellerIds.Where(id => people.GetValueOrDefault(id)?.PanLast4 is null))
                w.Add($"{Name(seller)} has no PAN on record — TDS needs the seller's PAN.");
        }

        if (d.ExecutionDate is null)
            w.Add("The agreement's date is not set.");
        if (d.ExecutionPlace is null)
            w.Add("The place of signing is not set.");
        if (d.Witnesses.Count < 2)
            w.Add("Two witnesses are usual; fewer are listed.");
        if (d.Kind == PropertyPurpose.Sale && d.FinalDate is null && d.Schedule.All(x => x.DueDate is null))
            w.Add("No last date for the balance payment and the transfer.");
        if (p.SizeSqFt is null && d.PropertyDescription is null)
            w.Add("The property's area is not filled.");
        foreach (var id in d.SellerIds.Concat(d.BuyerIds).Where(id => people.GetValueOrDefault(id) is { } x && (x.Address is null || x.AadhaarLast4 is null)))
            w.Add($"{Name(id)}: address or Aadhaar missing for the agreement.");
        return w;
    }

    public static string? RelationLabel(RelationKind? relation) => relation switch
    {
        RelationKind.SonOf => "S/O",
        RelationKind.WifeOf => "W/O",
        RelationKind.DaughterOf => "D/O",
        RelationKind.CareOf => "C/O",
        _ => null,
    };
}
