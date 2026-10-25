using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ZeroNova_Packages.Messaging;
using ZeroNova_Packages.Security;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain;
using ZeroNovaRealEstate.Infrastructure.Context;
using ZeroNovaRealEstate.Infrastructure.Persistence;
using ZeroNovaRealEstate.Infrastructure.Repositories;
using ZeroNovaRealEstate.Models.Configuration;
using ZeroNovaRealEstate.PubSub;

namespace ZeroNovaRealEstate.Infrastructure;

public static class InfrastructureInstaller
{
    /// <summary>Everything below the Api: Mongo, BSON mapping, the request context, messaging and repositories.</summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddDomainInstaller();
        services.AddMongo(configuration);
        // Producers (the agents' bells) from the PubSub assembly; the outbox and unit of work come from here.
        services.AddZeroNovaMessaging(configuration, typeof(PubSubAssembly).Assembly);

        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IRequirementRepository, RequirementRepository>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IEstateSettingsRepository, EstateSettingsRepository>();
        services.AddScoped<ICustomFieldRepository, CustomFieldRepository>();
        services.AddScoped<ILocalityRepository, LocalityRepository>();
        services.AddScoped<ICounterRepository, CounterRepository>();
        services.AddHostedService<IndexInitializer>();
        // After the indexes (hosted services start in order): holds that ran out go back to available.
        services.AddHostedService<HoldReleaseJob>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(configuration.GetSection(DocumentsOptions.SectionName).Get<DocumentsOptions>() ?? new DocumentsOptions());

        // Aadhaar and PAN are sealed with Core's master key (D11). A missing or placeholder key leaves the service up.
        var encryption = configuration.GetSection(EncryptionOptions.SectionName).Get<EncryptionOptions>() ?? new EncryptionOptions();
        services.AddSingleton<ISecretProtector>(sp => SecretProtectorOrNone(encryption.Key,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("ZeroNovaRealEstate.Encryption")));

        return services;
    }

    /// <summary>
    /// The master key, or none when it is missing or not a base64 32-byte key (a placeholder): the service still starts
    /// and only identity numbers say they cannot be saved yet.
    /// </summary>
    public static ISecretProtector SecretProtectorOrNone(string? key, ILogger logger)
    {
        try
        {
            return new AesGcmSecretProtector(key);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            logger.LogError("Encryption:Key is not a base64 32-byte key; Aadhaar and PAN cannot be saved until it is Core's master key.");
            return new AesGcmSecretProtector(null);
        }
    }
}
