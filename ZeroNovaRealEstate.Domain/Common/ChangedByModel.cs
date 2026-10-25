using MongoDB.Bson.Serialization.Attributes;

namespace ZeroNovaRealEstate.Domain.Common;

/// <summary>
/// Audit stamp embedded on every document. Filled by the repository base from the current request,
/// or with <see cref="System"/> for background / webhook writes that have no caller.
/// </summary>
public class ChangedByModel
{
    [BsonElement("changedById")]
    public required string ChangedById { get; set; }

    [BsonElement("changedByName")]
    public required string ChangedByName { get; set; }

    [BsonElement("changedByEmail")]
    public required string ChangedByEmail { get; set; }

    [BsonElement("changedDate")]
    public DateTimeOffset ChangedDate { get; set; }

    public static ChangedByModel System() => new()
    {
        ChangedById = "SYSTEM",
        ChangedByName = "SYSTEM",
        ChangedByEmail = "system@zeronova",
        ChangedDate = DateTimeOffset.UtcNow,
    };
}
