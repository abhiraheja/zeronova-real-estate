using System.Reflection;
using HotChocolate.Execution.Configuration;
using ZeroNovaRealEstate.Api.Graphql;
using ZeroNovaRealEstate.Api.Graphql.Base;
using ZeroNovaRealEstate.Api.Helpers;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Api.Installers;

public static class GraphqlInstaller
{
    /// <summary>
    /// The Real Estate service's GraphQL schema. Resolver classes extend <see cref="Query"/>/<see cref="Mutation"/> via
    /// <c>[ExtendObjectType]</c> and derive from <see cref="QueryBase"/>/<see cref="MutationBase"/> (found by reflection).
    /// Field names carry the <c>ZeroNova_</c> prefix, like Core's, so the gateway can stitch both without clashes.
    /// </summary>
    public static IServiceCollection AddGraphqlApi(this IServiceCollection services, IWebHostEnvironment env, IConfiguration configuration)
    {
        var options = configuration.GetSection(GraphqlOptions.SectionName).Get<GraphqlOptions>() ?? new GraphqlOptions();

        services.AddScoped<CurrentUser>();
        services.AddSingleton<HotChocolate.Execution.IErrorFilter, ValidationErrorFilter>();

        var builder = services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType(d => d.Name(nameof(Query)))
            .ModifyRequestOptions(o =>
            {
                o.IncludeExceptionDetails = env.IsDevelopment() || options.IncludeExceptionDetails;
                if (options.ExecutionTimeoutSeconds > 0)
                    o.ExecutionTimeout = TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds);
            });

        RegisterResolvers(services, builder);
        builder.DisableIntrospection(!(env.IsDevelopment() || options.AllowIntrospection));
        return services;
    }

    private static void RegisterResolvers(IServiceCollection services, IRequestExecutorBuilder builder)
    {
        var resolvers = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => !t.IsAbstract && (t.IsAssignableTo(typeof(QueryBase)) || t.IsAssignableTo(typeof(MutationBase))))
            .ToList();

        if (resolvers.Any(t => t.IsAssignableTo(typeof(MutationBase))))
            builder.AddMutationType(d => d.Name(nameof(Mutation)));

        foreach (var resolver in resolvers)
        {
            services.AddScoped(resolver);
            builder.AddType(resolver);
        }
    }
}
