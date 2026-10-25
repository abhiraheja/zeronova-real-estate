using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Constants;

namespace ZeroNovaRealEstate.Infrastructure.Context;

/// <summary><see cref="IRequestContext"/> over the current request's validated principal (anonymous in message handlers).</summary>
public sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? AuthUserId => Claim(ClaimNames.Subject);

    public string? UserId => Claim(ClaimNames.UserId);

    public string? Email => Claim(ClaimNames.Email);

    public string? CompanyId => Claim(ClaimNames.CompanyId);

    public IReadOnlyCollection<string> Permissions =>
        (Claim(ClaimNames.Permissions) ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string? Claim(string type) =>
        Principal?.FindFirst(type)?.Value is { Length: > 0 } value ? value : null;
}
