using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ZeroNovaRealEstate.Models.Constants;

namespace ZeroNovaRealEstate.Api.Authorization;

/// <summary>
/// The company has the Real Estate industry, and the token's <c>permissions</c> claim contains at least one of these codes.
/// </summary>
public sealed class PermissionRequirement(params string[] codes) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Codes { get; } = codes;
}

/// <summary>
/// Two checks on the validated token, in this order:
/// <list type="number">
/// <item>The <c>products</c> claim holds <c>REAL_ESTATE</c> (Core T16). This whole service is the Real Estate industry, so a company
/// without it is refused everything here — admin <c>*</c> included, because the admin's wildcard is about roles, not
/// about which industry the company runs.</item>
/// <item>Exact, case-insensitive match against the space-separated <c>permissions</c> claim; <c>*</c> grants every code.</item>
/// </list>
/// Reads only the validated token and holds no state, so it is a singleton.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Claimed(context, ClaimNames.Products).Contains(Products.RealEstate))
        {
            context.Fail(new AuthorizationFailureReason(this, "This company does not run the Real Estate industry."));
            return Task.CompletedTask;
        }

        var granted = Claimed(context, ClaimNames.Permissions);

        // The built-in admin role carries `*` instead of a list (Core decision O2).
        if (granted.Contains(EstatePermissions.Wildcard) || requirement.Codes.Any(granted.Contains))
            context.Succeed(requirement);
        else
            context.Fail(new AuthorizationFailureReason(this,
                $"One of the permissions [{string.Join(", ", requirement.Codes)}] is required."));

        return Task.CompletedTask;
    }

    private static HashSet<string> Claimed(AuthorizationHandlerContext context, string claim) =>
        context.User.FindAll(claim)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Builds <c>Permission:{codes}</c> policies on demand; every other name goes to the default provider.</summary>
public sealed class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var prefix = AuthPolicies.PermissionPrefix + ":";
        if (!policyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return await base.GetPolicyAsync(policyName);

        var codes = policyName[prefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.CompanyId)        // permissions are always per company
            .AddRequirements(new PermissionRequirement(codes))
            .Build();
    }
}
