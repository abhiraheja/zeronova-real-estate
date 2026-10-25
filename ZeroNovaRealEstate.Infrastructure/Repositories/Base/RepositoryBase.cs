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

/// <summary>Base for platform-global repositories. Soft delete, audit stamps, optimistic concurrency.</summary>
public abstract class RepositoryBase<TDocument> : IRepositoryBase<TDocument>
    where TDocument : DocumentBase
{
    protected readonly IMongoCollection<TDocument> Collection;
    protected readonly IRequestContext Context;

    protected RepositoryBase(IMongoDatabase database, IRequestContext context)
    {
        Collection = database.GetCollection<TDocument>(MongoCollectionNames.For(typeof(TDocument)));
        Context = context;
    }

    public virtual string GetNewId => ObjectId.GenerateNewId().ToString();

    public async Task<string> AddAsync(TDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(document.Id)) document.Id = GetNewId;
        document.CreatedBy ??= Stamp();
        document.Version = 0;

        await AmbientWrites.InsertAsync(Collection, document, ct);
        return document.Id;
    }

    public async Task<string> UpdateAsync(TDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(document.Id)) throw new ValidatorException("Id is required to update.");

        var filter = Builders<TDocument>.Filter.Eq(d => d.Id, document.Id);
        await ReplaceVersionedAsync(Collection, filter, document, Stamp(), ct);
        return document.Id;
    }

    public async Task<string> DeleteAsync(string id, CancellationToken ct = default)
    {
        var update = Builders<TDocument>.Update
            .Set(d => d.IsDeleted, true)
            .Set(d => d.UpdatedBy, Stamp())
            .Inc(d => d.Version, 1);

        var result = await AmbientWrites.UpdateOneAsync(
            Collection, Builders<TDocument>.Filter.Where(d => d.Id == id && !d.IsDeleted), update, ct);
        if (result.MatchedCount == 0) throw new ValidatorException("Document not found.");
        return id;
    }

    public IQueryable<TDocument> GetDocuments(bool includeDeleted = false)
    {
        var query = Collection.AsQueryable();
        return includeDeleted ? query : query.Where(d => !d.IsDeleted);
    }

    public IQueryable<TDocument> GetDocument(string id, bool includeDeleted = false) =>
        GetDocuments(includeDeleted).Where(d => d.Id == id);

    public IQueryable<TDocument> GetDocuments(Expression<Func<TDocument, bool>> predicate, bool includeDeleted = false) =>
        GetDocuments(includeDeleted).Where(predicate);

    public virtual IExecutable<TResult> AsExecutable<TResult>(
        Expression<Func<TDocument, TResult>> selector,
        Expression<Func<TDocument, bool>>? predicate = null,
        bool includeDeleted = false)
    {
        var query = GetDocuments(includeDeleted);
        if (predicate is not null) query = query.Where(predicate);
        return query.Select(selector).AsExecutable();
    }

    /// <summary>Audit stamp for the current caller, or SYSTEM when there is none (webhook, job).</summary>
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

    /// <summary>
    /// Replaces the document only if its stored version is still the one it was read at, writing the
    /// next version. On a miss, rolls the in-memory version back (so a caller's retry can still match)
    /// and tells "gone" apart from "changed by someone else".
    /// </summary>
    internal static async Task ReplaceVersionedAsync(
        IMongoCollection<TDocument> collection,
        FilterDefinition<TDocument> identity,
        TDocument document,
        ChangedByModel stamp,
        CancellationToken ct)
    {
        var expected = document.Version;
        document.Version = expected + 1;
        document.UpdatedBy = stamp;

        var filter = Builders<TDocument>.Filter.And(identity, Builders<TDocument>.Filter.Eq(d => d.Version, expected));
        ReplaceOneResult result;
        try
        {
            result = await AmbientWrites.ReplaceAsync(collection, filter, document, ct);
        }
        catch
        {
            // A unit of work retries its whole block on a transient error — the retry must expect the same version.
            document.Version = expected;
            throw;
        }
        if (result.MatchedCount > 0)
            return;

        document.Version = expected;
        var exists = await AmbientWrites.AnyAsync(collection, identity, ct);
        throw new ValidatorException(exists
            ? "This record was changed by someone else. Reload and try again."
            : "Document not found.");
    }
}
