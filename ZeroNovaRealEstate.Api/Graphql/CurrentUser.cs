using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Interfaces;

namespace ZeroNovaRealEstate.Api.Graphql;

/// <summary>
/// Resolver-side view of the caller: guard helpers over <see cref="IRequestContext"/>. Resolvers use
/// these to pull ids out of the token and pass them into commands — handlers never read claims.
/// </summary>
public sealed class CurrentUser(IRequestContext context)
{
    public IRequestContext Context => context;

    public string RequireUserId() => context.UserId
        ?? throw new ValidatorException("Your account is not set up yet. Sign in again.");

    public string RequireCompanyId() => context.CompanyId
        ?? throw new ValidatorException("No active company. Select a company first.");
}
