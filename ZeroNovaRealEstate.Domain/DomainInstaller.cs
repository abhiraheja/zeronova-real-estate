using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using ZeroNovaRealEstate.Domain.Helper;

namespace ZeroNovaRealEstate.Domain;

/// <summary>
/// BSON mapping for the Domain assembly, configured once at startup before the first Mongo read.
/// <para>
/// Ignore-extra-elements is a <b>convention over the whole assembly</b>, not a per-document flag: it
/// must also cover embedded types, which the driver auto-maps with <c>IgnoreExtraElements = false</c>.
/// Without it, removing a field from an embedded model makes every stored document that still carries
/// the key fail to deserialize (lesson from SaarAuth-V3, where that took down every login).
/// </para>
/// </summary>
public static class DomainInstaller
{
    public static IServiceCollection AddDomainInstaller(this IServiceCollection services)
    {
        var domainAssembly = Assembly.GetExecutingAssembly();

        // Conventions apply at auto-map time, so this must run BEFORE any class map is built.
        ConventionRegistry.Register(
            "ZeroNovaRealEstate.Domain.Conventions",
            new ConventionPack { new IgnoreExtraElementsConvention(true) },
            t => t.Assembly == domainAssembly);

        // Money and sizes are searched by range (price ≤ budget, size ≥ min): stored as numbers, never as strings.
        BsonSerializer.TryRegisterSerializer(new MongoDB.Bson.Serialization.Serializers.DecimalSerializer(MongoDB.Bson.BsonType.Decimal128));
        BsonSerializer.TryRegisterSerializer(new MongoDB.Bson.Serialization.Serializers.NullableSerializer<decimal>(
            new MongoDB.Bson.Serialization.Serializers.DecimalSerializer(MongoDB.Bson.BsonType.Decimal128)));

        // Registering document roots up front keeps the first read off the lazy auto-map path.
        var documentTypes = domainAssembly.GetTypes()
            .Where(t => !t.IsAbstract && t.GetCustomAttribute<DocumentNameAttribute>() is not null);

        foreach (var type in documentTypes)
        {
            if (BsonClassMap.IsClassMapRegistered(type))
                continue;

            var map = new BsonClassMap(type);
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            BsonClassMap.RegisterClassMap(map);
        }

        return services;
    }
}
