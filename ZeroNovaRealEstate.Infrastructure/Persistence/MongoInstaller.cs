using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

public static class MongoInstaller
{
    /// <summary>
    /// One <see cref="IMongoClient"/> for the whole process (thread-safe, owns the pool) and one
    /// database of this service (database per service). Fails fast at startup when unconfigured, rather than on the
    /// first request.
    /// </summary>
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>() ?? new MongoOptions();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                "Mongo:ConnectionString is not configured. It comes from Google Secret Manager: secret " +
                "Core's 'zeronovacore--Mongo--ConnectionString', read through GoogleSecretManager:SharedSecrets in the environment's project.");
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new InvalidOperationException("Mongo:DatabaseName is not configured.");

        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.MaxConnectionPoolSize = 100;
            settings.MinConnectionPoolSize = 5;
            settings.ConnectTimeout = TimeSpan.FromSeconds(10);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(30);
            settings.RetryWrites = true;
            settings.RetryReads = true;
            settings.ReadPreference = ReadPreference.Primary;
            settings.WriteConcern = WriteConcern.WMajority;
            settings.ReadConcern = ReadConcern.Majority;

            // OpenTelemetry activities. CaptureCommandText=false → no PII in traces.
            settings.ClusterConfigurator = cb =>
                cb.Subscribe(new DiagnosticsActivityEventSubscriber(new InstrumentationOptions
                {
                    CaptureCommandText = false,
                }));

            return new MongoClient(settings);
        });

        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName));

        return services;
    }
}
