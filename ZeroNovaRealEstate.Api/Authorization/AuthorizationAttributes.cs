using System.Reflection;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace ZeroNovaRealEstate.Api.Authorization;

public static class AuthPolicies
{
    /// <summary>Prefix of the dynamic permission policies: <c>Permission:CODE1,CODE2</c> (any-of).</summary>
    public const string PermissionPrefix = "Permission";

}

/// <summary>
/// Gates a resolver (or whole resolver class) by permission code(s). Several codes in ONE attribute = any-of; stacking
/// attributes = all-of. Use <c>EstatePermissions</c>, never raw strings. Always requires an active company and the Real Estate industry.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(params string[] codes) : DescriptorAttribute
{
    protected override void TryConfigure(IDescriptorContext context, IDescriptor descriptor, ICustomAttributeProvider? element)
    {
        var policy = $"{AuthPolicies.PermissionPrefix}:{string.Join(",", codes)}";
        switch (descriptor)
        {
            case IObjectFieldDescriptor field:
                field.Authorize(policy);
                break;
            case IObjectTypeDescriptor type:
                type.Authorize(policy);
                break;
        }
    }
}
