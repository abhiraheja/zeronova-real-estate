using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Deals;

/// <summary>
/// Adds (no id) or changes a deal (M3). Sellers default to the property's current owners; the transfer kind sets the
/// checklist; the money and the parties are checked and the warnings come back with the deal.
/// </summary>
public sealed record SaveDealCommand(string? DealId, DealInput Input) : IRequest<DealDto>;

public sealed class SaveDealValidator : AbstractValidator<SaveDealCommand>
{
    public SaveDealValidator()
    {
        RuleFor(x => x.Input.PropertyId).NotEmpty();
        RuleFor(x => x.Input.Price).GreaterThan(0);
        RuleFor(x => x.Input.EarnestPercent).InclusiveBetween(0, 100).When(x => x.Input.EarnestPercent is not null);
        RuleFor(x => x.Input.Payments).Must(p => p is null || p.All(x => x.Amount > 0)).WithMessage("A payment must be more than zero.");
        RuleFor(x => x.Input.Schedule).Must(p => p is null || p.All(x => x.Amount > 0)).WithMessage("An instalment must be more than zero.");
        RuleFor(x => x.Input.Notes).MaximumLength(4000);
    }
}

public sealed class SaveDealHandler(IDealRepository deals, IPropertyRepository properties, IPartyRepository parties, ICounterRepository counters, DealMapper mapper,
    EstateScope scope, Parties.PartyContacts contacts, Customers.CustomerBook customers)
    : IRequestHandler<SaveDealCommand, DealDto>
{
    public const string CodeCounter = "deal";

    public async Task<DealDto> Handle(SaveDealCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var property = await properties.GetDocument(input.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var isNew = string.IsNullOrEmpty(request.DealId);
        var deal = isNew
            ? new DealDocument { Id = deals.GetNewId, Code = $"D-{await counters.NextAsync(CodeCounter, ct):0000}", PropertyId = property.Id, AgentId = scope.UserId }
            : await DealLookup.RequireAsync(deals, scope, request.DealId!, ct);
        if (deal.Stage is DealStage.Completed or DealStage.Cancelled)
            throw new ValidatorException($"{deal.Code} is {deal.Stage.ToString().ToLowerInvariant()}; it cannot change.");
        if (isNew)
            DealLock.EnsureOpen(property);

        var sellers = (input.SellerIds is { Count: > 0 } s ? s : property.Owners.Select(o => o.PartyId)).Distinct().ToList();
        var buyers = input.BuyerIds.Distinct().ToList();
        var all = sellers.Concat(buyers).Distinct().ToList();
        if (await parties.GetDocuments(p => all.Contains(p.Id)).CountAsync(ct) != all.Count)
            throw new ValidatorException("A seller or buyer was not found. Add the person first.");
        if (sellers.Intersect(buyers).Any())
            throw new ValidatorException("The same person cannot be both seller and buyer.");

        var agent = EstateText.Clean(input.AgentId) ?? deal.AgentId;
        if (agent != scope.UserId && agent != deal.AgentId && !scope.SeesAllDeals)
            throw new ValidatorException("Only a manager can give a deal to another agent.");

        deal.PropertyId = property.Id;
        deal.Kind = property.Purpose;
        deal.SellerIds = sellers;
        deal.BuyerIds = buyers;
        deal.RequirementId = EstateText.Clean(input.RequirementId);
        deal.LeadId = EstateText.Clean(input.LeadId);
        deal.AgentId = agent;
        deal.Price = Math.Round(input.Price, 2);
        deal.EarnestPercent = input.EarnestPercent;
        deal.Payments = [.. (input.Payments ?? []).Select(p => new DealPaymentModel
        {
            Mode = p.Mode, Amount = Math.Round(p.Amount, 2), Reference = EstateText.Clean(p.Reference), Bank = EstateText.Clean(p.Bank),
            Date = EstateText.Date(p.Date, "Payment date"), PayerPartyId = EstateText.Clean(p.PayerPartyId),
        })];
        deal.Schedule = [.. (input.Schedule ?? []).Select(i => new DealInstalmentModel
        {
            Mode = i.Mode, Amount = Math.Round(i.Amount, 2), DueDate = EstateText.Date(i.DueDate, "Due date"),
        })];
        deal.FinalDate = EstateText.Date(input.FinalDate, "Final date");
        var kind = input.TransferKind ?? (property.Purpose == PropertyPurpose.Rent ? TransferKind.Rent : deal.TransferKind);
        if (deal.Checklist.Count == 0 || kind != deal.TransferKind)
            deal.Checklist = Checklists.For(kind);
        deal.TransferKind = kind;
        deal.ExecutionPlace = EstateText.Clean(input.ExecutionPlace);
        deal.ExecutionDate = EstateText.Date(input.ExecutionDate, "Execution date");
        deal.Witnesses = [.. (input.Witnesses ?? []).Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .Select(w => new WitnessModel { Name = EstateText.Clean(w.Name)!, Address = EstateText.Clean(w.Address) })];
        deal.PropertyDescription = EstateText.Clean(input.PropertyDescription);
        deal.TitleNote = EstateText.Clean(input.TitleNote);
        deal.Inclusions = [.. (input.Inclusions ?? []).Select(EstateText.Clean).OfType<string>()];
        deal.Terms = [.. (input.Terms ?? []).Select(t => t?.Trim()).OfType<string>().Where(t => t.Length > 0)];
        deal.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;

        if (isNew)
            await deals.AddAsync(deal, ct);
        else
            await deals.UpdateAsync(deal, ct);
        var rent = deal.Kind == PropertyPurpose.Rent;
        await contacts.SyncAsync(sellers, rent ? Parties.PartyContacts.Landlord : Parties.PartyContacts.Seller, ct);
        await contacts.SyncAsync(buyers, rent ? Parties.PartyContacts.Tenant : Parties.PartyContacts.Buyer, ct);
        await customers.TouchDealAsync(deal, ct);
        return await mapper.OneAsync(deal, ct);
    }
}

/// <summary>
/// Moves a deal along. Completed: the buyers become the owners (a sale) with the sellers moved into the history, the
/// property is sold or rented, and the customer's requirement is closed. Cancelled needs a reason.
/// </summary>
public sealed record SetDealStageCommand(string DealId, DealStageInput Input) : IRequest<DealDto>;

public sealed class SetDealStageHandler(IDealRepository deals, IPropertyRepository properties, IPartyRepository parties, IRequirementRepository requirements,
    MatchEngine engine, DealMapper mapper, EstateScope scope, TimeProvider clock, Parties.PartyContacts contacts, Customers.CustomerBook customers)
    : IRequestHandler<SetDealStageCommand, DealDto>
{
    public async Task<DealDto> Handle(SetDealStageCommand request, CancellationToken ct)
    {
        var deal = await DealLookup.RequireAsync(deals, scope, request.DealId, ct);
        if (deal.Stage is DealStage.Completed or DealStage.Cancelled)
            throw new ValidatorException($"{deal.Code} is already {deal.Stage.ToString().ToLowerInvariant()}.");
        var stage = request.Input.Stage;
        if (stage == DealStage.Cancelled)
            deal.CancelReason = EstateText.Clean(request.Input.Reason) ?? throw new ValidatorException("Say why the deal fell through.");

        // The double-sale lock: offers may run side by side, but only one deal holds the token and goes on from there.
        var held = DealLock.Holds(deal.Stage);
        if (DealLock.Holds(stage) || stage == DealStage.Completed)
        {
            var property = await properties.GetDocument(deal.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
            if (!held)
                DealLock.EnsureOpen(property);
            var holder = await deals.GetDocuments(d => d.PropertyId == deal.PropertyId && d.Id != deal.Id
                && (d.Stage == DealStage.Token || d.Stage == DealStage.Agreement || d.Stage == DealStage.Transfer || d.Stage == DealStage.Registry))
                .FirstOrDefaultAsync(ct);
            if (holder is not null)
                throw new ValidatorException($"{holder.Code} already has the token for {property.Code}. Cancel it first, or work that deal.");
            if (DealLock.Holds(stage) && !held && property.Status is not PropertyStatus.TokenReceived)
            {
                property.Status = PropertyStatus.TokenReceived;
                property.HoldUntil = null;
                await properties.UpdateAsync(property, ct);
                await engine.DropForPropertyAsync(property.Id, ct);
            }
        }
        else if (stage == DealStage.Cancelled && held
            && await properties.GetDocument(deal.PropertyId).FirstOrDefaultAsync(ct) is { Status: PropertyStatus.TokenReceived } released)
        {
            // The token is gone with the deal: the property is free again and is offered to matching customers.
            released.Status = PropertyStatus.Available;
            released.HoldUntil = null;
            await properties.UpdateAsync(released, ct);
            await engine.ForPropertyAsync(released, ct);
        }

        if (stage == DealStage.Completed)
        {
            if (deal.BuyerIds.Count == 0)
                throw new ValidatorException("Add the buyer before completing the deal.");
            var property = await properties.GetDocument(deal.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
            var ids = deal.SellerIds.Concat(deal.BuyerIds).ToList();
            var names = (await parties.GetDocuments(p => ids.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
            var today = EstateText.Today(clock);

            if (deal.Kind == PropertyPurpose.Sale)
            {
                property.History.Add(new OwnershipRecordModel
                {
                    FromPartyIds = deal.SellerIds, ToPartyIds = deal.BuyerIds, FromNames = [.. deal.SellerIds.Select(id => names.GetValueOrDefault(id) ?? "—")],
                    ToNames = [.. deal.BuyerIds.Select(id => names.GetValueOrDefault(id) ?? "—")], Kind = deal.TransferKind, Date = today, DealId = deal.Id,
                });
                var holding = Checklists.HoldingAfter(deal.TransferKind);
                var shares = Properties.SetPropertyOwnersHandler.Shares([.. deal.BuyerIds.Select(id => new PropertyOwnerInput(id, null, holding))]);
                property.Owners = [.. deal.BuyerIds.Zip(shares, (id, share) => new OwnerModel { PartyId = id, SharePercent = share, Holding = holding })];
                property.OwnerPhones = [.. (await parties.GetDocuments(p => deal.BuyerIds.Contains(p.Id)).Select(p => p.Phone).ToListAsync(ct)).OfType<string>()];
                property.Status = PropertyStatus.Sold;
            }
            else
                property.Status = PropertyStatus.Rented;
            property.HoldUntil = null;
            await properties.UpdateAsync(property, ct);
            await engine.DropForPropertyAsync(property.Id, ct);

            var leadId = deal.LeadId;
            if (deal.RequirementId is { } requirementId && await requirements.GetDocument(requirementId).FirstOrDefaultAsync(ct) is { } requirement)
            {
                leadId ??= requirement.LeadId;
                requirement.Status = RequirementStatus.Closed;
                requirement.CloseReason = deal.Kind == PropertyPurpose.Sale ? $"Bought {property.Code} through us" : $"Rented {property.Code} through us";
                requirement.CloseReasonCode = deal.Kind == PropertyPurpose.Sale ? RequirementCloseReason.BoughtThroughUs : RequirementCloseReason.RentedThroughUs;
                requirement.ClosedAt = clock.GetUtcNow().UtcDateTime;
                requirement.WatchUntil = null;
                await requirements.UpdateAsync(requirement, ct);
                await engine.ForRequirementAsync(requirement, ct);
            }
            deal.CompletedAt = clock.GetUtcNow().UtcDateTime;

            // The other offers on it fell through with this sale or let.
            var others = await deals.GetDocuments(d => d.PropertyId == deal.PropertyId && d.Id != deal.Id
                && d.Stage != DealStage.Completed && d.Stage != DealStage.Cancelled).ToListAsync(ct);
            foreach (var other in others)
            {
                other.Stage = DealStage.Cancelled;
                other.CancelReason = $"{property.Code} went to {deal.Code}";
                await deals.UpdateAsync(other, ct);
                await customers.TouchDealAsync(other, ct);
            }

            // The buyer's enquiry became a deal: owners of what they bought, and won in zeronova-lead.
            if (deal.Kind == PropertyPurpose.Sale)
                await contacts.WinAsync(deal.BuyerIds, [Parties.PartyContacts.Buyer, Parties.PartyContacts.Owner], $"Bought {property.Code} ({deal.Code})", leadId, ct);
            else
                await contacts.WinAsync(deal.BuyerIds, [Parties.PartyContacts.Tenant], $"Rented {property.Code} ({deal.Code})", leadId, ct);
        }

        deal.Stage = stage;
        await deals.UpdateAsync(deal, ct);
        await customers.TouchDealAsync(deal, ct);
        return await mapper.OneAsync(deal, ct);
    }
}

/// <summary>Ticks (or unticks) a step of the transfer checklist, with its date and a note.</summary>
public sealed record UpdateDealChecklistCommand(string DealId, ChecklistUpdateInput Input) : IRequest<DealDto>;

public sealed class UpdateDealChecklistHandler(IDealRepository deals, DealMapper mapper, EstateScope scope) : IRequestHandler<UpdateDealChecklistCommand, DealDto>
{
    public async Task<DealDto> Handle(UpdateDealChecklistCommand request, CancellationToken ct)
    {
        var deal = await DealLookup.RequireAsync(deals, scope, request.DealId, ct);
        var item = deal.Checklist.FirstOrDefault(c => c.Key == request.Input.Key) ?? throw new ValidatorException("No such step on this deal.");
        item.Done = request.Input.Done;
        item.Date = request.Input.Done ? EstateText.Date(request.Input.Date, "Date") : null;
        item.Note = EstateText.Clean(request.Input.Note);
        await deals.UpdateAsync(deal, ct);
        return await mapper.OneAsync(deal, ct);
    }
}

public sealed record GetDealsQuery(DealStage? Stage, string? PropertyId, int Take = 200) : IRequest<IReadOnlyList<DealDto>>;

public sealed class GetDealsHandler(IDealRepository deals, DealMapper mapper, EstateScope scope) : IRequestHandler<GetDealsQuery, IReadOnlyList<DealDto>>
{
    public async Task<IReadOnlyList<DealDto>> Handle(GetDealsQuery request, CancellationToken ct)
    {
        var query = deals.GetDocuments();
        if (!scope.SeesAllDeals)
        {
            var me = scope.UserId;
            query = query.Where(d => d.AgentId == me);
        }
        if (request.Stage is { } stage)
            query = query.Where(d => d.Stage == stage);
        if (EstateText.Clean(request.PropertyId) is { } propertyId)
            query = query.Where(d => d.PropertyId == propertyId);
        return await mapper.ManyAsync(await query.OrderByDescending(d => d.Id).Take(Math.Clamp(request.Take, 1, 500)).ToListAsync(ct), ct);
    }
}

public sealed record GetDealQuery(string DealId) : IRequest<DealDto>;

public sealed class GetDealHandler(IDealRepository deals, DealMapper mapper, EstateScope scope) : IRequestHandler<GetDealQuery, DealDto>
{
    public async Task<DealDto> Handle(GetDealQuery request, CancellationToken ct) => await mapper.OneAsync(await DealLookup.RequireAsync(deals, scope, request.DealId, ct), ct);
}

/// <summary>An earlier change of owners, entered from the papers (D8) — names, how, when; added before the deal-made ones.</summary>
public sealed record AddOwnershipRecordCommand(string PropertyId, OwnershipRecordInput Input) : IRequest<IReadOnlyList<OwnershipRecordDto>>;

public sealed class AddOwnershipRecordHandler(IPropertyRepository properties) : IRequestHandler<AddOwnershipRecordCommand, IReadOnlyList<OwnershipRecordDto>>
{
    public async Task<IReadOnlyList<OwnershipRecordDto>> Handle(AddOwnershipRecordCommand request, CancellationToken ct)
    {
        var property = await properties.GetDocument(request.PropertyId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var input = request.Input;
        var record = new OwnershipRecordModel
        {
            FromNames = [.. (input.FromNames ?? []).Select(EstateText.Clean).OfType<string>()],
            ToNames = [.. (input.ToNames ?? []).Select(EstateText.Clean).OfType<string>()],
            Kind = input.Kind, Date = EstateText.Date(input.Date, "Date"), Note = EstateText.Clean(input.Note),
        };
        if (record.ToNames.Count == 0)
            throw new ValidatorException("Name who the property passed to.");
        property.History = [.. property.History.Append(record).OrderBy(h => h.Date ?? "9999")];
        await properties.UpdateAsync(property, ct);
        return [.. property.History.Select(h => new OwnershipRecordDto(h.FromNames, h.ToNames, h.Kind, h.Date, h.Note, h.DealId))];
    }
}

/// <summary>
/// The transfer steps for each kind (design 10.10). C5 (D38): an authority (GMADA) property's steps go in order — NDC →
/// sale permission → registry → re-transfer → mutation — with the usual days as hints; GMADA LOI transfer, Chandigarh
/// leasehold and Chandigarh freehold are kinds of their own. A deal keeps the checklist it was made with.
/// </summary>
public static class Checklists
{
    private const string TdsLabel = "TDS deposited and certificate given (Form 26QB and 16B; 27Q and 16A for a non-resident seller)";

    public static List<ChecklistItemModel> For(TransferKind kind) => kind switch
    {
        TransferKind.BuilderAllotment =>
        [
            Item("builder_no_dues", "Builder no-dues certificate"), Item("builder_noc", "Builder NOC for transfer"), Item("transfer_fee", "Transfer fee paid"),
            Item("affidavits", "Buyer affidavits and indemnity"), Item("fresh_allotment", "Fresh allotment letter / BBA in buyer's name"),
            Item("tds", TdsLabel), Item("possession", "Possession and documents handed over"), Item("conveyance", "Conveyance deed (later)"),
        ],
        TransferKind.AuthorityPlot =>
        [
            Step(1, "ndc", "No Due Certificate (NDC) from GMADA / the authority", 15, "About 15 working days. All dues must be clear — check the property's ledger and status first."),
            Step(2, "sale_permission", "Sale permission from GMADA / the authority", 10, "About 10 days."),
            Step(3, "registry", "Sale deed registered at the Sub-Registrar", null,
                "Note the deed (vasika) number. Punjab's Easy Registry: any Sub-Registrar in the district, an online slot."),
            Step(4, "re_transfer", "Re-transfer in GMADA / authority records", 5,
                "About 5 working days: the re-transfer form and a certified copy of the deed. The registry alone does not make the buyer the authority's recognised holder."),
            Step(5, "mutation", "Mutation (intkal) in the revenue record", 30, "Note the mutation number. Punjab's online intkal aims at about 30 days after the registry."),
            Item("tds", TdsLabel), Item("possession", "Possession handed over"),
        ],
        TransferKind.GmadaLoi =>
        [
            Step(1, "ndc", "No Due Certificate (NDC) from GMADA", 15, "About 15 working days. All instalments of the LOI must be paid."),
            Step(2, "loi_transfer", "LOI transfer by GMADA", 21,
                "About 21 working days. Fee about 2.5% of the allotment price (not the market price; PUDA booklet 2019 — the fees change every year). A land-pooling LOI has no allotment price: ask GMADA."),
            Step(3, "loi_letter", "LOI / allotment papers in the buyer's name", null, null),
            Item("tds", TdsLabel), Item("documents", "Original LOI, receipts and papers handed over"),
        ],
        TransferKind.ChandigarhLeasehold =>
        [
            Step(1, "estate_permission", "Transfer permission from the Estate Office", null, "The Sub-Registrar will not register a leasehold sale deed without it."),
            Step(2, "unearned_increase", "Unearned increase paid to the Estate Office", null,
                "About one-third of the rise in value since allotment — estimate it before the price is fixed."),
            Step(3, "registry", "Sale deed registered at the Sub-Registrar", null, "Note the deed number."),
            Step(4, "mutation", "Mutation in the Estate Office records", null, "The Estate Office mutates from the registered deed — check it is done."),
            Item("tds", TdsLabel), Item("possession", "Possession handed over"),
        ],
        TransferKind.ChandigarhFreehold =>
        [
            Step(1, "conveyance", "Seller's conveyance deed from the Estate Office checked", null,
                "A freehold property passes by a registered deed only once the seller holds the Estate Office's conveyance deed; some co-operative society flats have none."),
            Step(2, "registry", "Sale deed registered at the Sub-Registrar", null, "Note the deed number."),
            Step(3, "mutation", "Mutation in the Estate Office records", null, "The Estate Office mutates from the registered deed — check it is done."),
            Item("tds", TdsLabel), Item("possession", "Possession handed over"),
        ],
        TransferKind.Registered =>
        [
            Item("title", "Title chain and encumbrance certificate checked"), Item("loan", "Seller's loan closed / bank NOC"),
            Item("tds", TdsLabel), Item("registry", "Sale deed registered"), Item("mutation", "Mutation done"),
            Item("possession", "Possession handed over"),
        ],
        _ =>
        [
            Item("agreement", "Rent agreement signed"), Item("deposit", "Security deposit received"), Item("police", "Police verification of tenant"),
            Item("keys", "Keys handed over"), Item("inventory", "Fittings list signed"),
        ],
    };

    /// <summary>How the buyers hold the property once the deal completes.</summary>
    public static HoldingKind HoldingAfter(TransferKind kind) => kind switch
    {
        TransferKind.BuilderAllotment => HoldingKind.BuilderAllotment,
        TransferKind.AuthorityPlot or TransferKind.GmadaLoi or TransferKind.ChandigarhLeasehold => HoldingKind.AuthorityAllotment,
        _ => HoldingKind.RegisteredSaleDeed,
    };

    private static ChecklistItemModel Item(string key, string label) => new() { Key = key, Label = label };

    private static ChecklistItemModel Step(int step, string key, string label, int? days, string? hint) =>
        new() { Key = key, Label = label, Step = step, TypicalDays = days, Hint = hint };
}

internal static class DealLookup
{
    public static async Task<DealDocument> RequireAsync(IDealRepository deals, EstateScope scope, string id, CancellationToken ct)
    {
        var deal = await deals.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Deal not found.");
        if (!scope.SeesAllDeals && deal.AgentId != scope.UserId)
            throw new ValidatorException("This deal is another agent's.");
        return deal;
    }
}

/// <summary>
/// The double-sale lock (C5's open item, the QA report of 2026-11-11): several deals may sit at Offer on one property,
/// but only one at Token or later; a deal never starts or moves on a property already sold, rented or withdrawn.
/// </summary>
internal static class DealLock
{
    /// <summary>The stages that hold the property for one buyer: the token is paid and the deal goes on.</summary>
    public static readonly DealStage[] HoldingStages = [DealStage.Token, DealStage.Agreement, DealStage.Transfer, DealStage.Registry];

    public static bool Holds(DealStage stage) => HoldingStages.Contains(stage);

    public static void EnsureOpen(PropertyDocument property)
    {
        var state = property.Status switch
        {
            PropertyStatus.Sold => "sold",
            PropertyStatus.Rented => "rented",
            PropertyStatus.Withdrawn => "withdrawn",
            _ => null,
        };
        if (state is not null)
            throw new ValidatorException($"{property.Code} is {state}. Mark it available again before starting or moving a deal on it.");
    }
}
