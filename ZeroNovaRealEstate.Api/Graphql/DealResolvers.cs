using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Agreements;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Models.Deals;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate M3: deals (an agent's own; every deal with PDLV), the transfer checklist, owners' history, and agreements
/// in Word from the ready templates or the dealer's own — printing opens Aadhaar and PAN, so it also needs POWV.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class DealQuery(IMediator mediator) : QueryBase
{
    [RequirePermission(P.DealView, P.DealManage)]
    [GraphQLName("ZeroNova_estateDeals")]
    public Task<IReadOnlyList<DealDto>> Deals(DealStage? stage, string? propertyId, CancellationToken ct) => mediator.Send(new GetDealsQuery(stage, propertyId), ct);

    [RequirePermission(P.DealView, P.DealManage)]
    [GraphQLName("ZeroNova_estateDeal")]
    public Task<DealDto> Deal(string dealId, CancellationToken ct) => mediator.Send(new GetDealQuery(dealId), ct);

    [RequirePermission(P.DealView, P.DealManage, P.SettingsManage)]
    [GraphQLName("ZeroNova_estateAgreementTemplates")]
    public Task<IReadOnlyList<AgreementTemplateDto>> AgreementTemplates(CancellationToken ct) => mediator.Send(new GetAgreementTemplatesQuery(), ct);

    [RequirePermission(P.DealManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_estateAgreementPreview")]
    public Task<AgreementPreviewDto> AgreementPreview(string dealId, string templateId, CancellationToken ct) =>
        mediator.Send(new PreviewAgreementQuery(dealId, templateId), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class DealMutation(IMediator mediator) : MutationBase
{
    [RequirePermission(P.DealManage)]
    [GraphQLName("ZeroNova_saveEstateDeal")]
    public Task<DealDto> SaveDeal(string? dealId, DealInput input, CancellationToken ct) => mediator.Send(new SaveDealCommand(dealId, input), ct);

    [RequirePermission(P.DealManage)]
    [GraphQLName("ZeroNova_setEstateDealStage")]
    public Task<DealDto> SetDealStage(string dealId, DealStageInput input, CancellationToken ct) => mediator.Send(new SetDealStageCommand(dealId, input), ct);

    [RequirePermission(P.DealManage)]
    [GraphQLName("ZeroNova_updateEstateDealChecklist")]
    public Task<DealDto> UpdateDealChecklist(string dealId, ChecklistUpdateInput input, CancellationToken ct) =>
        mediator.Send(new UpdateDealChecklistCommand(dealId, input), ct);

    /// <summary>An earlier change of owners, from the papers.</summary>
    [RequirePermission(P.PropertyManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_addEstateOwnershipRecord")]
    public Task<IReadOnlyList<OwnershipRecordDto>> AddOwnershipRecord(string propertyId, OwnershipRecordInput input, CancellationToken ct) =>
        mediator.Send(new AddOwnershipRecordCommand(propertyId, input), ct);

    [RequirePermission(P.DealManage)]
    [RequirePermission(P.OwnerView)]
    [GraphQLName("ZeroNova_buildEstateAgreement")]
    public Task<AgreementFileDto> BuildAgreement(string dealId, string templateId, string? editedText, CancellationToken ct) =>
        mediator.Send(new BuildAgreementCommand(dealId, templateId, editedText), ct);

    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_saveEstateAgreementTemplate")]
    public Task<AgreementTemplateDto> SaveAgreementTemplate(string? templateId, AgreementTemplateInput input, CancellationToken ct) =>
        mediator.Send(new SaveAgreementTemplateCommand(templateId, input), ct);

    [RequirePermission(P.SettingsManage)]
    [GraphQLName("ZeroNova_deleteEstateAgreementTemplate")]
    public Task<bool> DeleteAgreementTemplate(string templateId, CancellationToken ct) => mediator.Send(new DeleteAgreementTemplateCommand(templateId), ct);
}
