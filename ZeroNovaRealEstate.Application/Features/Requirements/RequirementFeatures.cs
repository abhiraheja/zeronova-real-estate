using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Requirements;

/// <summary>
/// Adds (no id) or changes what a customer wants (D9) and matches it against the stock at once (D10). An agent works
/// their own customers; a manager (PRQV) anyone's.
/// </summary>
public sealed record SaveRequirementCommand(string? RequirementId, RequirementInput Input) : IRequest<RequirementDto>;

public sealed class SaveRequirementValidator : AbstractValidator<SaveRequirementCommand>
{
    public SaveRequirementValidator()
    {
        RuleFor(x => x.Input.LeadId).NotEmpty().WithMessage("Pick the customer (lead) first.");
        RuleFor(x => x.Input.Localities).Must(l => l is null || l.Count <= 20).WithMessage("At most 20 localities.");
        RuleFor(x => x.Input.BudgetMax).GreaterThanOrEqualTo(x => x.Input.BudgetMin)
            .When(x => x.Input.BudgetMin is not null && x.Input.BudgetMax is not null)
            .WithMessage("The budget's maximum must not be below its minimum.");
        RuleFor(x => x.Input.Notes).MaximumLength(2000);
    }
}

public sealed class SaveRequirementHandler(
    IRequirementRepository requirements, IEstateSettingsRepository settings, ILocalityRepository localities, IMatchRepository matches,
    MatchEngine engine, EstateScope scope)
    : IRequestHandler<SaveRequirementCommand, RequirementDto>
{
    public async Task<RequirementDto> Handle(SaveRequirementCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var s = await EstateSettings.LoadAsync(settings, ct);
        RequirementDocument requirement;
        if (string.IsNullOrEmpty(request.RequirementId))
            requirement = new RequirementDocument { Id = requirements.GetNewId, LeadId = string.Empty, AgentId = scope.UserId };
        else
        {
            requirement = await RequirementLookup.RequireAsync(requirements, request.RequirementId!, ct);
            scope.RequireWork(requirement);
        }

        var agent = EstateText.Clean(input.AgentId);
        if (agent is not null && agent != scope.UserId && !scope.SeesAllRequirements)
            throw new ValidatorException("Only a manager can give a customer to another agent.");
        requirement.AgentId = agent ?? requirement.AgentId;
        requirement.LeadId = input.LeadId.Trim();
        requirement.LeadName = EstateText.Clean(input.LeadName);
        requirement.LeadPhone = EstateText.Phone(input.LeadPhone);
        requirement.Purpose = input.Purpose;
        requirement.Types = [.. (input.Types ?? []).Distinct()];
        requirement.City = EstateText.Clean(input.City) ?? s.DefaultCity;
        requirement.Localities = [.. (input.Localities ?? []).Select(EstateText.Clean).OfType<string>().DistinctBy(EstateText.Key)];
        requirement.LocalityKeys = [.. requirement.Localities.Select(EstateText.Key)];
        requirement.BudgetMin = input.BudgetMin is > 0 ? input.BudgetMin : null;
        requirement.BudgetMax = input.BudgetMax is > 0 ? input.BudgetMax : null;
        requirement.SizeMin = AreaMath.Model(input.SizeMin, s);
        requirement.SizeMax = AreaMath.Model(input.SizeMax, s);
        requirement.Bhk = [.. (input.Bhk ?? []).Distinct().Order()];
        requirement.Facings = [.. (input.Facings ?? []).Distinct()];
        requirement.CornerOnly = input.CornerOnly;
        requirement.ParkFacingOnly = input.ParkFacingOnly;
        requirement.Floor = input.Floor;
        requirement.NeedsLift = input.NeedsLift;
        requirement.NeedsParking = input.NeedsParking;
        requirement.Furnishings = [.. (input.Furnishings ?? []).Distinct()];
        requirement.TenantType = input.Purpose == PropertyPurpose.Rent ? input.TenantType : null;
        requirement.NeededBy = EstateText.Date(input.NeededBy, "Needed by");
        requirement.Notes = input.Notes?.Trim() is { Length: > 0 } notes ? notes : null;

        if (string.IsNullOrEmpty(request.RequirementId))
            await requirements.AddAsync(requirement, ct);
        else
            await requirements.UpdateAsync(requirement, ct);
        foreach (var locality in requirement.Localities)
            await localities.EnsureAsync(locality, EstateText.Key(locality), requirement.City, ct);

        await engine.ForRequirementAsync(requirement, ct);
        return await RequirementMapping.ToDtoAsync(requirement, matches, ct);
    }
}

/// <summary>Closes (bought or rented — through the dealer or elsewhere), loses, pauses or reopens a requirement.</summary>
public sealed record CloseRequirementCommand(string RequirementId, CloseRequirementInput Input) : IRequest<RequirementDto>;

public sealed class CloseRequirementHandler(IRequirementRepository requirements, IMatchRepository matches, MatchEngine engine, EstateScope scope)
    : IRequestHandler<CloseRequirementCommand, RequirementDto>
{
    public async Task<RequirementDto> Handle(CloseRequirementCommand request, CancellationToken ct)
    {
        var requirement = await RequirementLookup.RequireAsync(requirements, request.RequirementId, ct);
        scope.RequireWork(requirement);
        requirement.Status = request.Input.Status;
        requirement.CloseReason = request.Input.Status is RequirementStatus.Closed or RequirementStatus.Lost ? EstateText.Clean(request.Input.Reason) : null;
        await requirements.UpdateAsync(requirement, ct);
        await engine.ForRequirementAsync(requirement, ct);
        return await RequirementMapping.ToDtoAsync(requirement, matches, ct);
    }
}

/// <summary>Requirements the caller may see: their own, or everyone's with PRQV; by lead, agent, status or name.</summary>
public sealed record GetRequirementsQuery(RequirementStatus? Status, string? LeadId, string? AgentId, string? Search, int Take = 100)
    : IRequest<IReadOnlyList<RequirementDto>>;

public sealed class GetRequirementsHandler(IRequirementRepository requirements, IMatchRepository matches, EstateScope scope)
    : IRequestHandler<GetRequirementsQuery, IReadOnlyList<RequirementDto>>
{
    public async Task<IReadOnlyList<RequirementDto>> Handle(GetRequirementsQuery request, CancellationToken ct)
    {
        var query = requirements.GetDocuments();
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            query = query.Where(r => r.AgentId == me);
        }
        else if (EstateText.Clean(request.AgentId) is { } agent)
            query = query.Where(r => r.AgentId == agent);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);
        if (EstateText.Clean(request.LeadId) is { } lead)
            query = query.Where(r => r.LeadId == lead);
        if (EstateText.Clean(request.Search)?.ToLowerInvariant() is { } search)
            query = query.Where(r => r.LeadName != null && r.LeadName.ToLower().Contains(search));
        var rows = await query.OrderByDescending(r => r.Id).Take(Math.Clamp(request.Take, 1, 500)).ToListAsync(ct);
        return await RequirementMapping.ManyAsync(rows, matches, ct);
    }
}

public sealed record GetRequirementQuery(string RequirementId) : IRequest<RequirementDto>;

public sealed class GetRequirementHandler(IRequirementRepository requirements, IMatchRepository matches, EstateScope scope)
    : IRequestHandler<GetRequirementQuery, RequirementDto>
{
    public async Task<RequirementDto> Handle(GetRequirementQuery request, CancellationToken ct)
    {
        var requirement = await RequirementLookup.RequireAsync(requirements, request.RequirementId, ct);
        scope.RequireWork(requirement);
        return await RequirementMapping.ToDtoAsync(requirement, matches, ct);
    }
}

internal static class RequirementLookup
{
    public static async Task<RequirementDocument> RequireAsync(IRequirementRepository requirements, string id, CancellationToken ct) =>
        await requirements.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Requirement not found.");
}

public static class RequirementMapping
{
    public static async Task<RequirementDto> ToDtoAsync(RequirementDocument r, IMatchRepository matches, CancellationToken ct) =>
        (await ManyAsync([r], matches, ct))[0];

    public static async Task<IReadOnlyList<RequirementDto>> ManyAsync(IReadOnlyList<RequirementDocument> rows, IMatchRepository matches, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var counts = (await matches.GetDocuments(m => ids.Contains(m.RequirementId) && m.Status == MatchStatus.New).Select(m => m.RequirementId).ToListAsync(ct))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        return [.. rows.Select(r => new RequirementDto(r.Id, r.LeadId, r.LeadName, r.LeadPhone, r.AgentId, r.Status, r.CloseReason, r.Purpose, r.Types,
            r.City, r.Localities, r.BudgetMin, r.BudgetMax, AreaMath.Dto(r.SizeMin), AreaMath.Dto(r.SizeMax), r.Bhk, r.Facings, r.CornerOnly,
            r.ParkFacingOnly, r.Floor, r.NeedsLift, r.NeedsParking, r.Furnishings, r.TenantType, r.NeededBy, r.Notes,
            counts.GetValueOrDefault(r.Id), r.CreatedBy?.ChangedDate))];
    }
}
