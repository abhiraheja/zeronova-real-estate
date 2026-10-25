namespace ZeroNovaRealEstate.Application.Interfaces;

/// <summary>
/// Who is calling, read from the validated Supabase token (the same token and claims as Core). The one place claims are
/// interpreted — repositories (audit stamp, tenant filter) read this instead of parsing <c>HttpContext.User</c>. Every
/// property is null/empty for an anonymous request (tracking pixel) or a message handler.
/// </summary>
public interface IRequestContext
{
    bool IsAuthenticated { get; }

    /// <summary>Supabase auth user id (token <c>sub</c>).</summary>
    string? AuthUserId { get; }

    /// <summary>ZeroNova user id (token <c>zn_uid</c>).</summary>
    string? UserId { get; }

    string? Email { get; }

    /// <summary>Active company (token <c>company_id</c>).</summary>
    string? CompanyId { get; }

    IReadOnlyCollection<string> Permissions { get; }
}
