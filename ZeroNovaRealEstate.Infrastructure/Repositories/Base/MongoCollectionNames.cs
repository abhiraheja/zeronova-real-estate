using System.Reflection;
using ZeroNovaRealEstate.Domain.Helper;

namespace ZeroNovaRealEstate.Infrastructure.Repositories.Base;

internal static class MongoCollectionNames
{
    /// <summary>The <c>[DocumentName]</c> of a document type. Required — a silent fallback to the CLR type
    /// name would quietly create a second, differently named collection.</summary>
    public static string For(Type documentType) =>
        documentType.GetCustomAttribute<DocumentNameAttribute>()?.Name
        ?? throw new InvalidOperationException($"{documentType.Name} has no [DocumentName] attribute.");
}
