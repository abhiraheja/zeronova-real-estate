using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Application.Features.Customers;
using ZeroNovaRealEstate.Models.Customers;
using P = ZeroNovaRealEstate.Models.Constants.EstatePermissions;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Real Estate customers (C2, D32): the Customers list and the Real Estate side of a customer's dashboard. The customer is
/// a zeronova-lead contact (D3) — its leads and activities the app reads from zeronova-lead (LEDV there). Customers follow
/// requirements (D39): an agent's own with PRQM, everyone's with PRQV; deals, tenancies and owned properties keep their
/// own permissions inside the dashboard.
/// </summary>
[ExtendObjectType(nameof(Query))]
public sealed class CustomerQuery(IMediator mediator) : QueryBase
{
    /// <summary>The latest activity first; <paramref name="take"/> up to 200 (50 by default).</summary>
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateCustomers")]
    public Task<EstateCustomerPageDto> Customers(EstateCustomerFilterInput? filter, int? skip, int? take, CancellationToken ct) =>
        mediator.Send(new GetCustomersQuery(filter ?? new EstateCustomerFilterInput(), skip ?? 0, take ?? 50), ct);

    /// <summary>By the contact and / or its leads (from <c>ZeroNova_contactLeads</c>); at least one of them.</summary>
    [RequirePermission(P.RequirementView, P.RequirementManage)]
    [GraphQLName("ZeroNova_estateCustomer")]
    public Task<EstateCustomerDashboardDto> Customer(string? contactId, IReadOnlyList<string>? leadIds, CancellationToken ct) =>
        mediator.Send(new GetCustomerQuery(contactId, leadIds), ct);
}
