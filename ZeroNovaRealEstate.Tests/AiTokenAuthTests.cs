using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ZeroNovaRealEstate.Api.Installers;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Infrastructure.Context;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Core T23 — this service trusts AI Executives' tokens beside Supabase's: one signed by Core's key, for this audience
/// and in date, reaches the endpoint with <c>IsAi</c>, the code and the run; anything else is refused. A person's token
/// still goes to the Supabase scheme.
/// </summary>
public sealed class AiTokenAuthTests : IAsyncLifetime
{
    private const string Issuer = "https://core.zrnova.com/ai";
    private const string Audience = "zeronova-services";
    private const string JwksUrl = "http://zeronova-core.test/.well-known/ai-jwks.json";

    private readonly ECDsa coreKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private WebApplication app = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "https://example.supabase.co",
            ["AiTokens:Issuer"] = Issuer,
            ["AiTokens:Audience"] = Audience,
            ["AiTokens:JwksUrl"] = JwksUrl,
        });
        builder.Services.AddSupabaseAuthentication(builder.Configuration);
        // Core's JWKS, served by a fake retriever: the real one fetches it over HTTP.
        var jwks = Jwks(coreKey);
        builder.Services.PostConfigure<JwtBearerOptions>(AiTokenAuthentication.Scheme, o =>
            o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                JwksUrl, new AiTokenAuthentication.JwksRetriever(Issuer), new StaticDocument(jwks)));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();

        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/who", (IRequestContext caller) => Results.Json(new
        {
            caller.IsAi,
            caller.AiCode,
            caller.ActorName,
            caller.RunId,
            caller.UserId,
            caller.CompanyId,
            display = caller.AiDisplayName(),
        })).RequireAuthorization();
        await app.StartAsync();
        http = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await app.DisposeAsync();
        coreKey.Dispose();
        otherKey.Dispose();
    }

    [Fact]
    public async Task An_AI_token_signed_by_Core_is_accepted_and_names_the_Executive()
    {
        var response = await Who(Token(coreKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("isAi").GetBoolean());
        Assert.Equal("WLT-AI-0002", body.GetProperty("aiCode").GetString());
        Assert.Equal("Riya", body.GetProperty("actorName").GetString());
        Assert.Equal("run-42", body.GetProperty("runId").GetString());
        Assert.Equal("6a0000000000000000000002", body.GetProperty("userId").GetString());
        Assert.Equal("6a0000000000000000000001", body.GetProperty("companyId").GetString());
        Assert.Equal("Riya · AI (WLT-AI-0002)", body.GetProperty("display").GetString());
    }

    [Fact]
    public async Task Another_key_a_wrong_audience_or_an_expired_token_is_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Who(Token(otherKey))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Who(Token(coreKey, audience: "authenticated"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Who(Token(coreKey, expires: DateTime.UtcNow.AddMinutes(-5)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Who("not-a-jwt")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Who(null)).StatusCode);
    }

    [Fact]
    public void Only_a_token_from_Cores_AI_issuer_goes_to_the_AI_scheme()
    {
        var options = new ZeroNovaRealEstate.Models.Configuration.AiTokenOptions { Issuer = Issuer, JwksUrl = JwksUrl };

        Assert.True(AiTokenAuthentication.IsAiToken("Bearer " + Token(coreKey), options));
        Assert.False(AiTokenAuthentication.IsAiToken("Bearer " + Token(coreKey, issuer: "https://example.supabase.co/auth/v1"), options));
        Assert.False(AiTokenAuthentication.IsAiToken("Bearer junk", options));
        Assert.False(AiTokenAuthentication.IsAiToken(null, options));
        Assert.False(AiTokenAuthentication.IsEnabled(new ZeroNovaRealEstate.Models.Configuration.AiTokenOptions()));
    }

    private Task<HttpResponseMessage> Who(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/who");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http.SendAsync(request);
    }

    private static string Token(ECDsa key, string issuer = Issuer, string audience = Audience, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddMinutes(10);
        var signing = new ECDsaSecurityKey(key) { KeyId = Base64UrlEncoder.Encode(new ECDsaSecurityKey(key).ComputeJwkThumbprint()) };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = exp.AddMinutes(-10),
            NotBefore = exp.AddMinutes(-10),
            Expires = exp,
            SigningCredentials = new SigningCredentials(signing, SecurityAlgorithms.EcdsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "6a0000000000000000000002",
                ["zn_uid"] = "6a0000000000000000000002",
                ["company_id"] = "6a0000000000000000000001",
                ["permissions"] = "LEDV LEDM",
                ["zn_actor"] = "ai",
                ["zn_ai_code"] = "WLT-AI-0002",
                ["name"] = "Riya",
                ["zn_run_id"] = "run-42",
            },
        });
    }

    /// <summary>Core's JWKS document for <paramref name="key"/>, as <c>/.well-known/ai-jwks.json</c> serves it.</summary>
    private static string Jwks(ECDsa key)
    {
        var p = key.ExportParameters(false);
        var kid = Base64UrlEncoder.Encode(new ECDsaSecurityKey(key).ComputeJwkThumbprint());
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "EC", ["crv"] = "P-256", ["x"] = Base64UrlEncoder.Encode(p.Q.X!), ["y"] = Base64UrlEncoder.Encode(p.Q.Y!),
                    ["kid"] = kid, ["use"] = "sig", ["alg"] = "ES256",
                },
            },
        });
    }

    private sealed class StaticDocument(string json) : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) => Task.FromResult(json);
    }
}
