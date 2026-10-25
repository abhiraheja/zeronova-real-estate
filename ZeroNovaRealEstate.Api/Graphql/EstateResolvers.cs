using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate (M1): the dealer's stock with its owners, customers' requirements, the matches between them, keeping the
/// stock true, the dealer's settings and fields, and the dashboard. Every field needs the Real Estate industry.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class EstateQuery(IMediator mediator) : QueryBase
{
    // ── Properties

    [RequirePermission(P.PropertyView, P.PropertyManage)]
    [GraphQLName("ZeroNova_estateProperties")]
    public Task<PropertyPageDto> Properties(PropertySearchInput? search, CancellationToken ct) =>
        mediator.Send(new GetPropertiesQuery(search ?? new PropertySearchInput()), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage)]
    [GraphQLName("ZeroNova_estateProperty")]
    public Task<PropertyDto> Property(string propertyId, CancellationToken ct) => mediator.Send(new GetPropertyQuery(propertyId), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage)]
    [GraphQLName("ZeroNova_estateStaleProperties")]
    public Task<IReadOnlyList<PropertyDto>> StaleProperties(int? take, CancellationToken ct) => mediator.Send(new GetStalePropertiesQuery(take ?? 50), ct);

    [RequirePermission(P.PropertyManage)]
    [GraphQLName("ZeroNova_estatePropertyDuplicates")]
    public Task<IReadOnlyList<DuplicateDto>> PropertyDuplicates(PropertyInput input, string? excludeId, IReadOnlyList<string>? ownerPhones, CancellationToken ct) =>
        mediator.Send(new GetDuplicatesQuery(input, excludeId, ownerPhones), ct);

    // ── People (owners)

    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_estateParties")]
    public Task<IReadOnlyList<PartyDto>> Parties(string? search, CancellationToken ct) => mediator.Send(new GetPartiesQuery(search), ct);

    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_estateParty")]
    public Task<PartyDto> Party(string partyId, CancellationToken ct) => mediator.Send(new GetPartyQuery(partyId), ct);

    // ── Requirements and matches (an agent's own; everyone's with PRQV)

    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateRequirements")]
    public Task<IReadOnlyList<RequirementDto>> Requirements(RequirementStatus? status, string? leadId, string? agentId, string? search, CancellationToken ct) =>
        mediator.Send(new GetRequirementsQuery(status, leadId, agentId, search), ct);

    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateRequirement")]
    public Task<RequirementDto> Requirement(string requirementId, CancellationToken ct) => mediator.Send(new GetRequirementQuery(requirementId), ct);

    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateMatches")]
    public Task<IReadOnlyList<MatchDto>> Matches(string? propertyId, string? requirementId, MatchStatus? status, CancellationToken ct) =>
        mediator.Send(new GetMatchesQuery(propertyId, requirementId, status), ct);

    // ── Settings, fields, localities, dashboard

    [RequirePermission(P.PropertyView, P.PropertyManage, P.RequirementView, P.RequirementManage, P.SettingsManage)]
    [GraphQLName("ZeroNova_estateSettings")]
    public Task<EstateSettingsDto> Settings(CancellationToken ct) => mediator.Send(new GetEstateSettingsQuery(), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.SettingsManage)]
    [GraphQLName("ZeroNova_estateCustomFields")]
    public Task<IReadOnlyList<CustomFieldDto>> CustomFields(CancellationToken ct) => mediator.Send(new GetCustomFieldsQuery(), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateLocalities")]
    public Task<IReadOnlyList<LocalityDto>> Localities(string? search, CancellationToken ct) => mediator.Send(new GetLocalitiesQuery(search), ct);

    [RequirePermission(P.PropertyView, P.PropertyManage, P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateDashboard")]
    public Task<EstateDashboardDto> Dashboard(CancellationToken ct) => mediator.Send(new GetEstateDashboardQuery(), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class EstateMutation(IMediator mediator) : MutationBase
{
    [RequirePermission(P.PropertyManage)]
    [GraphQLName("ZeroNova_saveEstateProperty")]
    public Task<PropertyDto> SaveProperty(string? propertyId, PropertyInput input, CancellationToken ct) =>
        mediator.Send(new SavePropertyCommand(propertyId, input), ct);

    /// <summary>"Still available?" answered: the status now, perhaps a new price, and how it was confirmed.</summary>
    [RequirePermission(P.PropertyManage)]
    [GraphQLName("ZeroNova_confirmEstateProperty")]
    public Task<PropertyDto> ConfirmProperty(string propertyId, ConfirmPropertyInput input, CancellationToken ct) =>
        mediator.Send(new ConfirmPropertyCommand(propertyId, input), ct);

    [RequirePermission(P.PropertyManage)]
    [GraphQLName("ZeroNova_deleteEstateProperty")]
    public Task<bool> DeleteProperty(string propertyId, CancellationToken ct) => mediator.Send(new DeletePropertyCommand(propertyId), ct);

    /// <summary>The current owners — needs both managing properties and seeing owners.</summary>
    [RequirePermission(P.PropertyManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_setEstatePropertyOwners")]
    public Task<PropertyDto> SetPropertyOwners(string propertyId, IReadOnlyList<PropertyOwnerInput> owners, CancellationToken ct) =>
        mediator.Send(new SetPropertyOwnersCommand(propertyId, owners), ct);

    [RequirePermission(P.PropertyManage, P.RequirementManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_saveEstateParty")]
    public Task<PartyDto> SaveParty(string? partyId, PartyInput input, CancellationToken ct) => mediator.Send(new SavePartyCommand(partyId, input), ct);

    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_saveEstateRequirement")]
    public Task<RequirementDto> SaveRequirement(string? requirementId, RequirementInput input, CancellationToken ct) =>
        mediator.Send(new SaveRequirementCommand(requirementId, input), ct);

    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_closeEstateRequirement")]
    public Task<RequirementDto> CloseRequirement(string requirementId, CloseRequirementInput input, CancellationToken ct) =>
        mediator.Send(new CloseRequirementCommand(requirementId, input), ct);

    [RequirePermission(P.RequirementManage)]
    [GraphQLName("ZeroNova_updateEstateMatch")]
    public Task<MatchDto> UpdateMatch(string matchId, MatchUpdateInput input, CancellationToken ct) => mediator.Send(new UpdateMatchCommand(matchId, input), ct);

    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_saveEstateSettings")]
    public Task<EstateSettingsDto> SaveSettings(EstateSettingsInput input, CancellationToken ct) => mediator.Send(new SaveEstateSettingsCommand(input), ct);

    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_saveEstateCustomField")]
    public Task<CustomFieldDto> SaveCustomField(string? fieldId, CustomFieldInput input, CancellationToken ct) =>
        mediator.Send(new SaveCustomFieldCommand(fieldId, input), ct);

    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_deleteEstateCustomField")]
    public Task<bool> DeleteCustomField(string fieldId, CancellationToken ct) => mediator.Send(new DeleteCustomFieldCommand(fieldId), ct);
}
