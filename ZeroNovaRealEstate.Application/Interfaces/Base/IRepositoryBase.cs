using System.Linq.Expressions;
using HotChocolate;
using HotChocolate.Data;
using ZeroNovaRealEstate.Domain.Base;

namespace ZeroNovaRealEstate.Application.Interfaces.Base;

/// <summary>Repository for platform-global documents (not owned by a company).</summary>
public interface IRepositoryBase<TDocument> where TDocument : DocumentBase
{
    string GetNewId { get; }

    Task<string> AddAsync(TDocument document, CancellationToken ct = default);

    /// <summary>Optimistic: fails with a conflict if the document changed since it was read.</summary>
    Task<string> UpdateAsync(TDocument document, CancellationToken ct = default);

    /// <summary>Soft delete (<c>isDeleted = true</c>).</summary>
    Task<string> DeleteAsync(string id, CancellationToken ct = default);

    IQueryable<TDocument> GetDocuments(bool includeDeleted = false);
    IQueryable<TDocument> GetDocument(string id, bool includeDeleted = false);
    IQueryable<TDocument> GetDocuments(Expression<Func<TDocument, bool>> predicate, bool includeDeleted = false);

    /// <summary>For GraphQL list resolvers — lets HotChocolate push filter/sort/paging down to Mongo.</summary>
    IExecutable<TResult> AsExecutable<TResult>(
        Expression<Func<TDocument, TResult>> selector,
        Expression<Func<TDocument, bool>>? predicate = null,
        bool includeDeleted = false);
}
