using System.Linq.Expressions;
using HotChocolate;
using HotChocolate.Data;
using MongoDB.Bson;
using MongoDB.Driver;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Application.Interfaces.Base;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Common;
using ZeroNova_Packages.Messaging.Mongo;

namespace ZeroNovaRealEstate.Infrastructure.Repositories.Base;

/// <summary>
/// Base for tenant-owned repositories. The company filter lives HERE — never in handlers — so it
/// cannot be forgotten. With no active company (and no explicit system company id) every call throws:
/// failing closed is better than reading across tenants or writing a document owned by nobody.
/// </summary>
public abstract class CompanyRepositoryBase<TDocument> : ICompanyRepositoryBase<TDocument>
    where TDocument : CompanyDocumentBase
{
    protected readonly IMongoCollection<TDocument> Collection;
    protected readonly IRequestContext Context;

    protected CompanyRepositoryBase(IMongoDatabase database, IRequestContext context)
    {
        Collection = database.GetCollection<TDocument>(MongoCollectionNames.For(typeof(TDocument)));
        Context = context;
    }

    public virtual string GetNewId => ObjectId.GenerateNewId().ToString();

    public async Task<string> AddAsync(TDocument document, CancellationToken ct = default, string? companyId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(document.Id)) document.Id = GetNewId;
        document.CompanyId = ResolveCompanyId(companyId);
        document.CreatedBy ??= Stamp();
        document.Version = 0;

        await AmbientWrites.InsertAsync(Collection, document, ct);
        return document.Id;
    }

    public async Task<string> UpdateAsync(TDocument document, CancellationToken ct = default, string? companyId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(document.Id)) throw new ValidatorException("Id is required to update.");

        var tenant = ResolveCompanyId(companyId);
        document.CompanyId = tenant;   // a document can never be moved to another company by an update

        var identity = Builders<TDocument>.Filter.And(
            Builders<TDocument>.Filter.Eq(d => d.Id, document.Id),
            Builders<TDocument>.Filter.Eq(d => d.CompanyId, tenant));

        await RepositoryBase<TDocument>.ReplaceVersionedAsync(Collection, identity, document, Stamp(), ct);
        return document.Id;
    }

    public async Task<string> DeleteAsync(string id, CancellationToken ct = default, string? companyId = null)
    {
        var tenant = ResolveCompanyId(companyId);
        var update = Builders<TDocument>.Update
            .Set(d => d.IsDeleted, true)
            .Set(d => d.UpdatedBy, Stamp())
            .Inc(d => d.Version, 1);

        var result = await AmbientWrites.UpdateOneAsync(
            Collection, Builders<TDocument>.Filter.Where(d => d.Id == id && d.CompanyId == tenant && !d.IsDeleted), update, ct);
        if (result.MatchedCount == 0) throw new ValidatorException("Document not found.");
        return id;
    }

    public IQueryable<TDocument> GetDocuments(bool includeDeleted = false, string? companyId = null)
    {
        var tenant = ResolveCompanyId(companyId);
        var query = Collection.AsQueryable().Where(d => d.CompanyId == tenant);
        return includeDeleted ? query : query.Where(d => !d.IsDeleted);
    }

    public IQueryable<TDocument> GetDocument(string id, bool includeDeleted = false, string? companyId = null) =>
        GetDocuments(includeDeleted, companyId).Where(d => d.Id == id);

    public IQueryable<TDocument> GetDocuments(
        Expression<Func<TDocument, bool>> predicate, bool includeDeleted = false, string? companyId = null) =>
        GetDocuments(includeDeleted, companyId).Where(predicate);

    public virtual IExecutable<TResult> AsExecutable<TResult>(
        Expression<Func<TDocument, TResult>> selector,
        Expression<Func<TDocument, bool>>? predicate = null,
        bool includeDeleted = false)
    {
        var query = GetDocuments(includeDeleted);
        if (predicate is not null) query = query.Where(predicate);
        return query.Select(selector).AsExecutable();
    }

    /// <summary>Explicit system company id wins (no-caller paths); otherwise the token's active company.</summary>
    protected string ResolveCompanyId(string? explicitCompanyId) =>
        explicitCompanyId is { Length: > 0 } system ? system
        : Context.CompanyId is { Length: > 0 } active ? active
        : throw new ValidatorException("No active company. Select a company first.");

    protected ChangedByModel Stamp() =>
        Context.IsAuthenticated
            ? new ChangedByModel
            {
                ChangedById = Context.UserId ?? Context.AuthUserId ?? "UNKNOWN",
                ChangedByName = Context.Email ?? "UNKNOWN",
                ChangedByEmail = Context.Email ?? "UNKNOWN",
                ChangedDate = DateTimeOffset.UtcNow,
            }
            : ChangedByModel.System();
}
