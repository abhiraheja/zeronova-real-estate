using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using ZeroNovaRealEstate.Api.Authorization;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Constants;

namespace ZeroNovaRealEstate.Api.Installers;

public static class AuthInstaller
{
    /// <summary>The default scheme: forwards to Supabase's or the AI Executives' (Core T23) by the token's issuer.</summary>
    public const string CallerScheme = "Caller";

    /// <summary>
    /// The Supabase access tokens the app already holds for Core — same project, same claims (Core's access-token hook
    /// adds <c>company_id</c>, <c>permissions</c>, <c>products</c>). Public keys come from OIDC discovery (ES256), so no
    /// secret is held for them.
    /// </summary>
    public static IServiceCollection AddSupabaseAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var supabase = configuration.GetSection(SupabaseOptions.SectionName).Get<SupabaseOptions>() ?? new SupabaseOptions();
        if (string.IsNullOrWhiteSpace(supabase.Url))
            throw new InvalidOperationException("Supabase:Url is not configured.");

        // Core T23: an AI Executive's token (Core's issuer) goes to the AI scheme; everything else to Supabase's, as before.
        var ai = configuration.GetSection(AiTokenOptions.SectionName).Get<AiTokenOptions>() ?? new AiTokenOptions();
        services.AddSingleton(ai);

        services.AddAuthentication(CallerScheme)
            .AddPolicyScheme(CallerScheme, "Supabase or AI Executive", options => options.ForwardDefaultSelector = context =>
                AiTokenAuthentication.Select(context, ai, JwtBearerDefaults.AuthenticationScheme))
            .AddAiTokens(ai)
            .AddJwtBearer(options =>
            {
                options.Authority = supabase.Issuer;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = supabase.Issuer,
                    ValidateAudience = true,
                    ValidAudience = supabase.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Subject,
                    // Supabase's `role` is its Postgres role, not ours — never map it to ASP.NET roles.
                    RoleClaimType = "zn_unused_role",
                };
            });

        services.AddAuthorization();

        // Dynamic `Permission:{codes}` policies behind [RequirePermission].
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}
