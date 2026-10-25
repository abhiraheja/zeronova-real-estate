namespace ZeroNovaRealEstate.Api.Graphql.Base;

/// <summary>GraphQL root query type. Resolver classes extend it via <c>[ExtendObjectType(nameof(Query))]</c>.</summary>
public class Query;

/// <summary>GraphQL root mutation type. Resolver classes extend it via <c>[ExtendObjectType(nameof(Mutation))]</c>.</summary>
public class Mutation;

/// <summary>Marker base for query resolver classes — picked up by reflection in GraphqlInstaller.</summary>
public abstract class QueryBase;

/// <summary>Marker base for mutation resolver classes — picked up by reflection in GraphqlInstaller.</summary>
public abstract class MutationBase;
