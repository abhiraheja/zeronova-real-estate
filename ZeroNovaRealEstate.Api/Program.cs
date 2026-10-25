using ZeroNovaRealEstate.Api.Installers;
using ZeroNovaRealEstate.Application;
using ZeroNovaRealEstate.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Secrets first, so every installer below sees them. One GCP project per environment.
builder.AddGoogleSecretManager();

// Structured logging, rate limiting and CI/CD are topic T00b (user-owned) — not wired here yet, same as Core.

builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddSupabaseAuthentication(builder.Configuration);
builder.Services.AddGraphqlApi(builder.Environment, builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "zeronova-real-estate").AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

app.MapGraphQL();

app.Run();
