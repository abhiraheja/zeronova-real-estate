using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Common;

namespace ZeroNovaRealEstate.Domain.Base;

/// <summary>
/// Base for every ZeroNova document. Platform-global documents extend this directly; tenant-owned
/// documents extend <see cref="CompanyDocumentBase"/>.
/// </summary>
public abstract class DocumentBase : IDocumentBase
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("createdBy")]
    public ChangedByModel? CreatedBy { get; set; }

    [BsonElement("updatedBy")]
    public ChangedByModel? UpdatedBy { get; set; }

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Optimistic-concurrency stamp. <c>UpdateAsync</c> only replaces the document if the stored
    /// version is still the one it was read at, then writes the next one — so two concurrent edits
    /// cannot silently overwrite each other (a whole-document <c>ReplaceOne</c> otherwise would).
    /// </summary>
    [BsonElement("version")]
    public long Version { get; set; }
}
