using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Deals;

/// <summary>
/// Deals as members see them: parties named (Aadhaar and PAN as their last four), money totalled, the TDS on every
/// payment and the stamp duty estimate (C5), and the checks before an agreement.
/// </summary>
public sealed class DealMapper(IPropertyRepository properties, IPartyRepository parties, DealCheckSource checks)
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
        var (settings, brokerages, today) = await checks.LoadAsync([.. rows.Select(d => d.Id)], ct);

        return [.. rows.Where(d => props.ContainsKey(d.PropertyId)).Select(d =>
        {
            var p = props[d.PropertyId];
            var context = DealCheckContext.For(p, settings, brokerages?[d.Id].ToList(), today);
            var tds = PropertyTds.For(d, p, people);
            var paid = d.Payments.Sum(x => x.Amount);
            return new DealDto(d.Id, d.Code, p.Id, p.Code, p.Title, d.Kind, d.Stage,
                [.. d.SellerIds.Select(id => Party(people, id))], [.. d.BuyerIds.Select(id => Party(people, id))],
                d.RequirementId, d.LeadId, d.AgentId, d.Price, IndianWords.Rupees(d.Price), d.EarnestPercent,
                d.EarnestPercent is { } pct ? Math.Round(d.Price * pct / 100m, 0) : null,
                [.. d.Payments.Select(x => new DealPaymentDto(x.Mode, x.Amount, IndianWords.Rupees(x.Amount), x.Reference, x.Bank, x.Date, x.PayerPartyId,
                    x.PayerPartyId is { } payer ? people.GetValueOrDefault(payer)?.Name : null, tds.On(x.Amount),
                    tds.ResidentDue ? PropertyTds.Section(x.Date, today) : null))],
                paid,
                [.. d.Schedule.Select(x => new DealInstalmentDto(x.Mode, x.Amount, IndianWords.Rupees(x.Amount), x.DueDate, tds.On(x.Amount),
                    tds.ResidentDue ? PropertyTds.Section(x.DueDate, today) : null))],
                d.Price - paid, d.FinalDate, d.TransferKind,
                [.. d.Checklist.Select(c => new ChecklistItemDto(c.Key, c.Label, c.Done, c.Date, c.Note, c.Step, c.TypicalDays, c.Hint))],
                d.ExecutionPlace, d.ExecutionDate, [.. d.Witnesses.Select(w => new WitnessDto(w.Name, w.Address))], d.PropertyDescription, d.TitleNote,
                d.Inclusions, d.Terms, d.Notes, tds.On(d.Price), DealChecks.Warnings(d, p, people, context),
                d.CompletedAt is { } c ? new DateTimeOffset(DateTime.SpecifyKind(c, DateTimeKind.Utc)) : null, d.CancelReason, d.CreatedBy?.ChangedDate)
            {
                State = context.State,
                TdsDetail = PropertyTds.Dto(tds, d, today),
                StampDuty = StampDuty.Estimate(d, p, people, context),
            };
        })];
    }

    private static DealPartyDto Party(IReadOnlyDictionary<string, PartyDocument> people, string id) =>
        people.GetValueOrDefault(id) is { } p
            ? new DealPartyDto(p.Id, p.Name, DealChecks.RelationLabel(p.Relation), p.RelativeName, p.AadhaarLast4, p.PanLast4, p.Address, p.Residency, p.Gender)
            : new DealPartyDto(id, "—", null, null, null, null, null);
}

/// <summary>
/// What to fix before an agreement is printed (from the owner's samples, design 10.9): every co-owner signs, the money
/// adds up, dates agree, no UTR is used twice, nothing is left blank, TDS and cash. C5 (D38): the TDS section and rate
/// by seller (20% without a PAN, s.195 for a non-resident), FEMA on farm land, GPA, ordered transfer steps, and
/// Haryana's brokerage cap (only for a caller who sees brokerage).
/// </summary>
public static class DealChecks
{
    public static IReadOnlyList<string> Warnings(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people, DealCheckContext x)
    {
        var w = new List<string>();
        string Name(string id) => people.GetValueOrDefault(id)?.Name ?? "Someone";

        foreach (var owner in p.Owners.Where(o => !d.SellerIds.Contains(o.PartyId)))
            w.Add($"{Name(owner.PartyId)} owns {owner.SharePercent:0.##}% of the property but is not a seller — every co-owner must sign.");
        w.AddRange(GpaCheck.Warnings(p, people));
        if (d.SellerIds.Count == 0)
            w.Add("No seller yet. Add the property's owners or the sellers.");
        if (d.BuyerIds.Count == 0)
            w.Add("No buyer yet.");
        w.AddRange(FemaCheck.Warnings(d, p, people));

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
        w.AddRange(TdsWarnings(d, p, people, x));

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

        // Ordered steps (GMADA: NDC → sale permission → registry → re-transfer → mutation) ticked out of turn.
        foreach (var done in d.Checklist.Where(c => c is { Done: true, Step: not null }))
            if (d.Checklist.Where(c => c.Step < done.Step && !c.Done).OrderBy(c => c.Step).FirstOrDefault() is { } skipped)
                w.Add($"\"{done.Label}\" is ticked before \"{skipped.Label}\" — these steps go in order.");

        if (x.Brokerages is { } brokerages)
            w.AddRange(HaryanaBrokerageCap.Warnings(d, x.State, brokerages));
        return w;
    }

    private static IEnumerable<string> TdsWarnings(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people, DealCheckContext x)
    {
        var tds = PropertyTds.For(d, p, people);
        if (tds.ResidentDue && tds.RatePercent is not null)
        {
            // Per seller, never one rate "on the full price": a non-resident's share is s.195 (its own line below).
            var section = PropertyTds.Section(d.ExecutionDate, x.Today);
            yield return $"TDS under {section}: {PropertyTds.Basis(tds)} ({IndianWords.Figures(tds.On(d.Price)!.Value)} in all) — the deal is ₹50 lakh or more "
                + "(all buyers and sellers together). The buyer deducts it from every payment, the token included.";
            foreach (var seller in tds.Sellers.Where(s => s is { NoPan: true, NonResident: false }))
                yield return $"{seller.Name} has no PAN on record — TDS on their share is 20%, not 1%.";
        }
        foreach (var seller in tds.Sellers.Where(s => s.NonResident))
            yield return $"{seller.Name} is a non-resident seller ({PropertyTds.ResidencyLabel(people.GetValueOrDefault(seller.PartyId)?.Residency)}) — "
                + $"{PropertyTds.NonResidentText}.";
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
