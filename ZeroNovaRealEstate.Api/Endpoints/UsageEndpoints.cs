using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZeroNovaRealEstate.Application.Features.Usage;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Api.Endpoints;

/// <summary>
/// <c>GET /internal/usage?companyId=&amp;days=</c> — one company's counts for Core (the portal's company dashboard and the
/// company's Usage page), called on the cluster network (<c>http://zeronova-real-estate:8080</c>). Contract: zeronova-portal
/// <c>docs/company-insights.md</c>, the same as lead, study, media, social and the office.
/// <para>
/// No JWT: the gate is Core's usage key in <c>X-Usage-Key</c> (<c>Usage:ServiceKey</c>), compared in constant time. Key not
/// configured → 404 (the route is off); missing or wrong key → 401. Reads only, counts only.
/// </para>
/// </summary>
public static class UsageEndpoints
{
    public const string Route = "/internal/usage";
    public const string KeyHeader = "X-Usage-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddUsageEndpoint(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UsageOptions>(configuration.GetSection(UsageOptions.SectionName));
        return services;
    }

    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, GetAsync).AllowAnonymous().ExcludeFromDescription();   // the key below is the gate, not a token
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext http, IOptions<UsageOptions> options, IEstateUsageSource usage, CancellationToken ct)
    {
        var expected = options.Value.ServiceKey;
        if (string.IsNullOrWhiteSpace(expected))
            return Results.NotFound();
        if (!KeyMatches(http.Request.Headers[KeyHeader].ToString(), expected))
            return Results.Unauthorized();

        var companyId = http.Request.Query["companyId"].ToString().Trim();
        if (companyId.Length is 0 or > 64)
            return Results.BadRequest(new { error = "companyId is required." });

        int? days = int.TryParse(http.Request.Query["days"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
        return Results.Json(await usage.GetAsync(companyId, days, ct), Json);
    }

    /// <summary>Constant time, whatever the lengths: both sides are hashed first.</summary>
    private static bool KeyMatches(string given, string expected) =>
        given.Length > 0
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
