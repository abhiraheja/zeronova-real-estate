using HotChocolate.Authorization;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Models.Visits;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate M2: site visits (an agent's own; everyone's with PRQV), share links that count their opens, the public
/// shared page, and ready WhatsApp messages sent from the dealer's own WhatsApp.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class VisitQuery(IMediator mediator) : QueryBase
{
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateVisits")]
    public Task<IReadOnlyList<VisitDto>> Visits(DateTimeOffset? from, DateTimeOffset? to, string? propertyId, string? requirementId, VisitStatus? status,
        CancellationToken ct) => mediator.Send(new GetVisitsQuery(from, to, propertyId, requirementId, status), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage)]
    [GraphQLName("ZeroNova_estateShares")]
    public Task<IReadOnlyList<ShareDto>> Shares(string propertyId, CancellationToken ct) => mediator.Send(new GetSharesQuery(propertyId), ct);

    /// <summary>The shared page — anyone with the link; the property as a customer may see it.</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_estateSharedProperty")]
    public Task<SharedPropertyDto> SharedProperty(string token, CancellationToken ct) => mediator.Send(new GetSharedPropertyQuery(token), ct);

    /// <summary>"Still available?" to the owner, ready for the dealer's WhatsApp.</summary>
    [RequirePermission(P.PropertyManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_estateOwnerCheckDraft")]
    public Task<WhatsAppDraftDto> OwnerCheckDraft(string propertyId, CancellationToken ct) => mediator.Send(new GetOwnerCheckDraftQuery(propertyId), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class VisitMutation(IMediator mediator) : MutationBase
{
    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_scheduleEstateVisit")]
    public Task<VisitDto> ScheduleVisit(VisitInput input, CancellationToken ct) => mediator.Send(new ScheduleVisitCommand(input), ct);

    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_rescheduleEstateVisit")]
    public Task<VisitDto> RescheduleVisit(string visitId, DateTimeOffset at, CancellationToken ct) => mediator.Send(new RescheduleVisitCommand(visitId, at), ct);

    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_recordEstateVisitResult")]
    public Task<VisitDto> RecordVisitResult(string visitId, VisitResultInput input, CancellationToken ct) =>
        mediator.Send(new RecordVisitResultCommand(visitId, input), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.RequirementManage)]
    [GraphQLName("ZeroNova_shareEstateProperty")]
    public Task<ShareDto> ShareProperty(string propertyId, string? requirementId, CancellationToken ct) =>
        mediator.Send(new CreateShareCommand(propertyId, requirementId), ct);
}
