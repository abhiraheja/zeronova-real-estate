namespace ZeroNovaRealEstate.Models.Constants;

/// <summary>
/// Claim names in the Supabase access token — the same token Core validates, with the claims Core's access-token hook
/// adds. Inbound claim mapping is OFF, so these are the literal JWT keys.
/// </summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";

    /// <summary>ZeroNova user id (<c>zn_uid</c>).</summary>
    public const string UserId = "zn_uid";

    /// <summary>The caller's active company. Every tenant-scoped read/write filters on it.</summary>
    public const string CompanyId = "company_id";

    /// <summary>Effective permission codes, space-separated — or <c>*</c> for the built-in admin role.</summary>
    public const string Permissions = "permissions";

    /// <summary>The company's products (Core T16): <c>BASIC</c> plus its industry, e.g. <c>"BASIC REAL_ESTATE"</c>.</summary>
    public const string Products = "products";
}

/// <summary>Core's product codes (T16). This whole service is the Real Estate industry.</summary>
public static class Products
{
    public const string RealEstate = "REAL_ESTATE";
}

/// <summary>
/// Real Estate permissions (design D14), defined in Core's permission catalog with <c>Product = REAL_ESTATE</c> (Core
/// mints the token and owns the role editor) and enforced here. Exact match; Manage does not imply View.
/// <see cref="Wildcard"/> grants everything.
/// </summary>
public static class EstatePermissions
{
    public const string Wildcard = "*";

    /// <summary>The dealer's stock: every member with it sees every property.</summary>
    public const string PropertyView = "PPRV";
    public const string PropertyManage = "PPRM";

    /// <summary>Owners' phone numbers, unit numbers, Aadhaar and PAN (masked unless printing an agreement).</summary>
    public const string OwnerView = "POWV";

    /// <summary>Every requirement and match (a manager); without it an agent sees only their own.</summary>
    public const string RequirementView = "PRQV";

    /// <summary>Add and change requirements — an agent's own, or anyone's with <see cref="RequirementView"/>.</summary>
    public const string RequirementManage = "PRQM";

    public const string SettingsManage = "PSEM";

    /// <summary>Every deal (a manager); without it a member sees only the deals they are the agent of (M3).</summary>
    public const string DealView = "PDLV";

    /// <summary>Make and move deals, tick transfer steps, print agreements — one's own deals, or anyone's with <see cref="DealView"/>.</summary>
    public const string DealManage = "PDLM";

    /// <summary>Brokerage: what is earned, invoiced, received and due (M4). Staff without it never see it.</summary>
    public const string BrokerageView = "PBRV";

    public const string BrokerageManage = "PBRM";

    /// <summary>Tenancies, rents and renewals (M4).</summary>
    public const string RentalView = "PRNV";

    public const string RentalManage = "PRNM";
}
