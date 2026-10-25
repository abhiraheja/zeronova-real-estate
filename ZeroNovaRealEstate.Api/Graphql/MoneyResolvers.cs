using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Brokerage;
using ZeroNovaRealEstate.Application.Features.Rentals;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Money;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate M4: brokerage (only members with PBRV or PBRM see any of it) and rentals — tenancies, rent received,
/// receipts, renewals (an agent's own; every tenancy with PRNV).
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class MoneyQuery(IMediator mediator) : QueryBase
{
    [RequirePermission(P.BrokerageView, P.BrokerageManage)]
    [GraphQLName("ZeroNova_estateBrokerages")]
    public Task<IReadOnlyList<BrokerageDto>> Brokerages(string? dealId, BrokerageStatus? status, CancellationToken ct) =>
        mediator.Send(new GetBrokeragesQuery(dealId, status), ct);

    [RequirePermission(P.BrokerageView, P.BrokerageManage)]
    [GraphQLName("ZeroNova_estateBrokerageSummary")]
    public Task<BrokerageSummaryDto> BrokerageSummary(string? from, string? to, CancellationToken ct) => mediator.Send(new GetBrokerageSummaryQuery(from, to), ct);

    [RequirePermission(P.RentalView, P.RentalManage)]
    [GraphQLName("ZeroNova_estateTenancies")]
    public Task<IReadOnlyList<TenancyDto>> Tenancies(TenancyStatus? status, string? propertyId, bool? rentDueOnly, CancellationToken ct) =>
        mediator.Send(new GetTenanciesQuery(status, propertyId, rentDueOnly ?? false), ct);

    [RequirePermission(P.RentalView, P.RentalManage)]
    [GraphQLName("ZeroNova_estateTenancy")]
    public Task<TenancyDto> Tenancy(string tenancyId, CancellationToken ct) => mediator.Send(new GetTenancyQuery(tenancyId), ct);
}

[ExtendObjectType(nameof(Mutation))]
public sealed class MoneyMutation(IMediator mediator) : MutationBase
{
    [RequirePermission(P.BrokerageManage)]
    [GraphQLName("ZeroNova_saveEstateBrokerage")]
    public Task<BrokerageDto> SaveBrokerage(string? brokerageId, BrokerageInput input, CancellationToken ct) =>
        mediator.Send(new SaveBrokerageCommand(brokerageId, input), ct);

    [RequirePermission(P.BrokerageManage)]
    [GraphQLName("ZeroNova_addEstateBrokerageReceipt")]
    public Task<BrokerageDto> AddBrokerageReceipt(string brokerageId, BrokerageReceiptInput input, CancellationToken ct) =>
        mediator.Send(new AddBrokerageReceiptCommand(brokerageId, input), ct);

    [RequirePermission(P.BrokerageManage)]
    [GraphQLName("ZeroNova_writeOffEstateBrokerage")]
    public Task<BrokerageDto> WriteOffBrokerage(string brokerageId, bool writtenOff, string? reason, CancellationToken ct) =>
        mediator.Send(new WriteOffBrokerageCommand(brokerageId, writtenOff, reason), ct);

    [RequirePermission(P.BrokerageView, P.BrokerageManage)]
    [GraphQLName("ZeroNova_buildEstateBrokerageInvoice")]
    public Task<AgreementFileDto> BuildBrokerageInvoice(string brokerageId, CancellationToken ct) => mediator.Send(new BuildBrokerageInvoiceCommand(brokerageId), ct);

    [RequirePermission(P.RentalManage)]
    [GraphQLName("ZeroNova_saveEstateTenancy")]
    public Task<TenancyDto> SaveTenancy(string? tenancyId, TenancyInput input, CancellationToken ct) => mediator.Send(new SaveTenancyCommand(tenancyId, input), ct);

    [RequirePermission(P.RentalManage)]
    [GraphQLName("ZeroNova_recordEstateRentPayment")]
    public Task<TenancyDto> RecordRentPayment(string tenancyId, RentPaymentInput input, CancellationToken ct) =>
        mediator.Send(new RecordRentPaymentCommand(tenancyId, input), ct);

    [RequirePermission(P.RentalManage)]
    [GraphQLName("ZeroNova_renewEstateTenancy")]
    public Task<TenancyDto> RenewTenancy(string tenancyId, int months, decimal? rent, CancellationToken ct) =>
        mediator.Send(new RenewTenancyCommand(tenancyId, months, rent), ct);

    [RequirePermission(P.RentalManage)]
    [GraphQLName("ZeroNova_endEstateTenancy")]
    public Task<TenancyDto> EndTenancy(string tenancyId, string? date, string? reason, CancellationToken ct) =>
        mediator.Send(new EndTenancyCommand(tenancyId, date, reason), ct);

    [RequirePermission(P.RentalView, P.RentalManage)]
    [GraphQLName("ZeroNova_buildEstateRentReceipt")]
    public Task<AgreementFileDto> BuildRentReceipt(string tenancyId, string month, CancellationToken ct) => mediator.Send(new BuildRentReceiptCommand(tenancyId, month), ct);
}
