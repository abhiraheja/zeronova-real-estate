using MongoDB.Bson.Serialization.Attributes;

namespace ZeroNovaRealEstate.Domain.Base;

/// <summary>
/// Base for tenant-owned documents. <c>CompanyRepositoryBase</c> stamps and filters
/// <see cref="CompanyId"/> on every read and write, so a handler can never see or touch another
/// company's data. Do not add more tenant state here.
/// </summary>
public abstract class CompanyDocumentBase : DocumentBase
{
    [BsonElement("companyId")]
    public string CompanyId { get; set; } = string.Empty;
}
