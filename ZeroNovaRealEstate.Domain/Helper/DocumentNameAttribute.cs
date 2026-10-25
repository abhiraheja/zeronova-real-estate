namespace ZeroNovaRealEstate.Domain.Helper;

/// <summary>
/// Declares the MongoDB collection for a document type. Read by the repository base (to resolve the
/// collection) and by <see cref="DomainInstaller"/> (to register class maps). This service has its own database;
/// names still carry an <c>re_</c> prefix so a dump or a shared cluster never confuses them.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DocumentNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
