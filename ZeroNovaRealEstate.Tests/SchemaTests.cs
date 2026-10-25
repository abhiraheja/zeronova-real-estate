using HotChocolate.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using ZeroNovaRealEstate.Api.Installers;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The GraphQL schema, built the way the API builds it. A bad resolver fails the whole schema (the whole service); this
/// also names the fields the app's operations rely on. Set ZN_ESTATE_SDL_OUT to a path to also write the SDL there (the
/// app's codegen reads it).
/// </summary>
public sealed class SchemaTests
{
    [Fact]
    public async Task Schema_builds_with_the_real_estate_fields()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphqlApi(new Env(), new ConfigurationBuilder().Build());

        var executor = await services.BuildServiceProvider().GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        var sdl = executor.Schema.ToString();

        foreach (var field in new[]
        {
            "ZeroNova_estateProperties(", "ZeroNova_estateProperty(", "ZeroNova_estateStaleProperties(", "ZeroNova_estatePropertyDuplicates(",
            "ZeroNova_estateParties(", "ZeroNova_estateParty(", "ZeroNova_estateRequirements(", "ZeroNova_estateRequirement(", "ZeroNova_estateMatches(",
            "ZeroNova_estateSettings", "ZeroNova_estateCustomFields", "ZeroNova_estateLocalities(", "ZeroNova_estateDashboard",
            "ZeroNova_saveEstateProperty(", "ZeroNova_confirmEstateProperty(", "ZeroNova_deleteEstateProperty(", "ZeroNova_setEstatePropertyOwners(",
            "ZeroNova_saveEstateParty(", "ZeroNova_saveEstateRequirement(", "ZeroNova_closeEstateRequirement(", "ZeroNova_updateEstateMatch(",
            "ZeroNova_saveEstateSettings(", "ZeroNova_saveEstateCustomField(", "ZeroNova_deleteEstateCustomField(",
            "BUILDER_FLOOR", "NORTH_EAST", "SQ_YD", "MARLA", "price: Decimal", "sizeSqFt: Decimal",
        })
            Assert.Contains(field, sdl);

        if (Environment.GetEnvironmentVariable("ZN_ESTATE_SDL_OUT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, sdl.EndsWith('\n') ? sdl : sdl + "\n");
    }

    private sealed class Env : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ZeroNovaRealEstate.Api";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
