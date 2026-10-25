using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Matching;

/// <summary>
/// Matches the caller may see (D15) — of a property, of a requirement, or all of theirs — best first. New ones first
/// unless a status is asked for.
/// </summary>
public sealed record GetMatchesQuery(string? PropertyId, string? RequirementId, MatchStatus? Status, int Take = 100) : IRequest<IReadOnlyList<MatchDto>>;

public sealed class GetMatchesHandler(IMatchRepository matches, IPropertyRepository properties, IRequirementRepository requirements, EstateScope scope)
    : IRequestHandler<GetMatchesQuery, IReadOnlyList<MatchDto>>
{
    public async Task<IReadOnlyList<MatchDto>> Handle(GetMatchesQuery request, CancellationToken ct)
    {
        var query = matches.GetDocuments();
        if (!scope.SeesAllRequirements)
        {
            var me = scope.UserId;
            query = query.Where(m => m.AgentId == me);
        }
        if (EstateText.Clean(request.PropertyId) is { } propertyId)
            query = query.Where(m => m.PropertyId == propertyId);
        if (EstateText.Clean(request.RequirementId) is { } requirementId)
            query = query.Where(m => m.RequirementId == requirementId);
        if (request.Status is { } status)
            query = query.Where(m => m.Status == status);
        var rows = await query.OrderByDescending(m => m.Score).ThenByDescending(m => m.CreatedAt).Take(Math.Clamp(request.Take, 1, 500)).ToListAsync(ct);
        return await MatchMapping.ManyAsync(rows, properties, requirements, ct);
    }
}

/// <summary>Marks a match shown (sent or told), interested, or rejected with the customer's reason.</summary>
public sealed record UpdateMatchCommand(string MatchId, MatchUpdateInput Input) : IRequest<MatchDto>;

public sealed class UpdateMatchHandler(IMatchRepository matches, IPropertyRepository properties, IRequirementRepository requirements, EstateScope scope, TimeProvider clock)
    : IRequestHandler<UpdateMatchCommand, MatchDto>
{
    public async Task<MatchDto> Handle(UpdateMatchCommand request, CancellationToken ct)
    {
        var match = await matches.GetDocument(request.MatchId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Match not found.");
        if (!scope.SeesAllRequirements && match.AgentId != scope.UserId)
            throw new ValidatorException("This customer is another agent's.");
        match.Status = request.Input.Status;
        match.RejectReason = request.Input.Status == MatchStatus.Rejected ? EstateText.Clean(request.Input.RejectReason) : null;
        match.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await matches.UpdateAsync(match, ct);
        return (await MatchMapping.ManyAsync([match], properties, requirements, ct))[0];
    }
}

public static class MatchMapping
{
    public static async Task<IReadOnlyList<MatchDto>> ManyAsync(IReadOnlyList<MatchDocument> rows, IPropertyRepository properties,
        IRequirementRepository requirements, CancellationToken ct, string? companyId = null)
    {
        if (rows.Count == 0)
            return [];
        var propertyIds = rows.Select(m => m.PropertyId).Distinct().ToList();
        var requirementIds = rows.Select(m => m.RequirementId).Distinct().ToList();
        var props = (await properties.GetDocuments(p => propertyIds.Contains(p.Id), true, companyId).ToListAsync(ct)).ToDictionary(p => p.Id);
        var reqs = (await requirements.GetDocuments(r => requirementIds.Contains(r.Id), true, companyId).ToListAsync(ct)).ToDictionary(r => r.Id);
        return [.. rows.Where(m => props.ContainsKey(m.PropertyId) && reqs.ContainsKey(m.RequirementId)).Select(m =>
        {
            var p = props[m.PropertyId];
            var r = reqs[m.RequirementId];
            return new MatchDto(m.Id, p.Id, p.Code, p.Title, p.PriceValue, p.Status, r.Id, r.LeadId, r.LeadName, r.LeadPhone, m.AgentId, m.Score,
                m.Reasons, m.Status, m.RejectReason, m.PriceWhenMatched, new DateTimeOffset(DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc)));
        })];
    }
}
