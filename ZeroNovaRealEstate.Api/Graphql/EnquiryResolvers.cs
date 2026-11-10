using HotChocolate.Authorization;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Models.Enquiries;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate enquiries (C1, D27–D31): every enquiry from every source — kept from zeronova-lead's
/// <c>lead-enquiry-received</c> — the share page's "Enquire", staff "Add enquiry", working an enquiry, the Enquiries screen
/// with day / week / month views. Enquiries follow requirements (D39): an agent's own with PRQM, everyone's with PRQV.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class EnquiryQuery(IMediator mediator) : QueryBase
{
    /// <summary>Newest first; <paramref name="take"/> up to 200 (50 by default).</summary>
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateEnquiries")]
    public Task<EnquiryPageDto> Enquiries(EnquiryFilterInput? filter, int? skip, int? take, CancellationToken ct) =>
        mediator.Send(new GetEnquiriesQuery(filter ?? new EnquiryFilterInput(), skip ?? 0, take ?? 50), ct);

    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateEnquiry")]
    public Task<EnquiryDto> Enquiry(string enquiryId, CancellationToken ct) => mediator.Send(new GetEnquiryQuery(enquiryId), ct);

    /// <summary>The enquiry "Add enquiry" sent, by the ref it answered with — null while zeronova-lead is still recording it.</summary>
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateEnquiryByRef")]
    public Task<EnquiryDto?> EnquiryByRef(string @ref, CancellationToken ct) => mediator.Send(new GetEnquiryByRefQuery(@ref), ct);

    /// <summary>Counts per India day, week (from Monday) or month, by source, open and closed. <paramref name="from"/> / <paramref name="to"/>: yyyy-MM-dd, both included.</summary>
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateEnquiryStats")]
    public Task<EnquiryStatsDto> EnquiryStats(EnquiryFilterInput? filter, EnquiryBucket bucket, string? from, string? to, CancellationToken ct) =>
        mediator.Send(new GetEnquiryStatsQuery(filter ?? new EnquiryFilterInput(), bucket, from, to), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class EnquiryMutation(IMediator mediator) : MutationBase
{
    /// <summary>The share page's "Enquire" form — anyone with the link (a property's or a project's).</summary>
    [AllowAnonymous]
    [GraphQLName("ZeroNova_enquireEstateShare")]
    public Task<ShareEnquiryAckDto> EnquireShare(string token, string name, string phone, string? message, CancellationToken ct) =>
        mediator.Send(new EnquireShareCommand(token, name, phone, message), ct);

    /// <summary>A call, walk-in, WhatsApp or other enquiry; recorded through zeronova-lead (poll by the answer's ref).</summary>
    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_addEstateEnquiry")]
    public Task<EnquiryAckDto> AddEnquiry(AddEnquiryInput input, CancellationToken ct) => mediator.Send(new AddEnquiryCommand(input), ct);

    /// <summary>Status, close with a reason, link a requirement or visit. Never touches the lead.</summary>
    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_updateEstateEnquiry")]
    public Task<EnquiryDto> UpdateEnquiry(string enquiryId, UpdateEnquiryInput input, CancellationToken ct) =>
        mediator.Send(new UpdateEnquiryCommand(enquiryId, input), ct);
}
