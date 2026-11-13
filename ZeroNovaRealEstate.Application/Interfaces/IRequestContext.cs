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

    // ── Who acts, when it is an AI Executive (Core T23) — a person's token leaves these empty ──────

    /// <summary>True on an AI Executive's token (<c>zn_actor = ai</c>). <see cref="UserId"/> is then the Executive's id.</summary>
    bool IsAi => false;

    /// <summary>The Executive's code, e.g. <c>WLT-AI-0002</c>.</summary>
    string? AiCode => null;

    /// <summary>The Executive's display name ("Riya").</summary>
    string? ActorName => null;

    /// <summary>The office run an AI token was issued for.</summary>
    string? RunId => null;
}

/// <summary>How an AI Executive is named in records (Core T23): <c>Riya · AI (WLT-AI-0002)</c> — never the name alone.</summary>
public static class RequestActor
{
    public static string? AiDisplayName(this IRequestContext context) =>
        context.IsAi ? $"{context.ActorName ?? "AI Executive"} · AI ({context.AiCode ?? "?"})" : null;
}
