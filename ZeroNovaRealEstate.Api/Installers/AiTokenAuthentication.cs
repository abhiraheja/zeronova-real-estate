using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.Models.Constants;

namespace ZeroNovaRealEstate.Api.Installers;

/// <summary>
/// Core T23: AI Executives act on their own token, which zeronova-core signs (ES256) — not Supabase. A second JwtBearer
/// scheme checks it against Core's JWKS; <see cref="Select"/> picks the scheme by the token's (unvalidated) issuer, and
/// the chosen scheme then validates issuer, audience, lifetime and signature in full. The claims are a person's
/// (<c>company_id</c>, <c>permissions</c>…) plus <c>zn_actor = ai</c>, <c>zn_ai_code</c>, <c>name</c>, <c>zn_run_id</c>,
/// so <c>[RequirePermission]</c> and tenant scoping work unchanged. Off while <c>AiTokens:JwksUrl</c> is empty.
/// </summary>
public static class AiTokenAuthentication
{
    public const string Scheme = "ZeroNovaAi";

    public static bool IsEnabled(AiTokenOptions options) => !string.IsNullOrWhiteSpace(options.JwksUrl);

    /// <summary>The scheme for this request's bearer token: Core's AI scheme for an AI token, else <paramref name="otherwise"/>.</summary>
    public static string Select(HttpContext context, AiTokenOptions options, string otherwise) =>
        IsEnabled(options) && IsAiToken(context.Request.Headers.Authorization.ToString(), options) ? Scheme : otherwise;

    /// <summary>Only routes the request: a token that merely claims the issuer still has to pass the AI scheme.</summary>
    public static bool IsAiToken(string? authorization, AiTokenOptions options)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return new JsonWebToken(authorization[prefix.Length..].Trim()).Issuer == options.Issuer;
        }
        catch (ArgumentException)
        {
            return false;   // not a JWT: the other scheme refuses it
        }
    }

    public static AuthenticationBuilder AddAiTokens(this AuthenticationBuilder builder, AiTokenOptions options)
    {
        if (!IsEnabled(options))
            return builder;
        return builder.AddJwtBearer(Scheme, jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.RequireHttpsMetadata = options.JwksUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            // A plain JWKS document, not OIDC discovery. Refreshed hourly, and at most every 30 s when a token names a key
            // it does not know yet (a rotation) — JwtBearer asks for that refresh itself.
            jwt.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                options.JwksUrl, new JwksRetriever(options.Issuer), new HttpDocumentRetriever { RequireHttps = jwt.RequireHttpsMetadata })
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(1),
                RefreshInterval = TimeSpan.FromSeconds(30),
            };
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = ClaimNames.Subject,
                RoleClaimType = "zn_unused_role",
            };
        });
    }

    /// <summary>Reads Core's JWKS into the configuration JwtBearer validates against.</summary>
    public sealed class JwksRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var json = await retriever.GetDocumentAsync(address, cancel);
            var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
            foreach (var key in new JsonWebKeySet(json).GetSigningKeys())
                configuration.SigningKeys.Add(key);
            return configuration;
        }
    }
}
