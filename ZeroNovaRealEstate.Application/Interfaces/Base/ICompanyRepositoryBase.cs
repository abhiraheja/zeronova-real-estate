using System.Linq.Expressions;
using HotChocolate;
using HotChocolate.Data;
using ZeroNovaRealEstate.Domain.Base;

namespace ZeroNovaRealEstate.Application.Interfaces.Base;

/// <summary>
/// Repository for tenant-owned documents. Every operation is scoped to the caller's active company
/// (token <c>company_id</c>): reads only return that company's documents, writes stamp it. A call
/// without an active company fails instead of reading or writing unscoped.
/// <para>
/// The explicit <c>companyId</c> overloads exist for system paths with no caller token (webhooks,
/// consumers, background jobs). Never pass a company id taken from client input.
/// </para>
/// </summary>
public interface ICompanyRepositoryBase<TDocument> where TDocument : CompanyDocumentBase
{
    string GetNewId { get; }

    Task<string> AddAsync(TDocument document, CancellationToken ct = default, string? companyId = null);
    Task<string> UpdateAsync(TDocument document, CancellationToken ct = default, string? companyId = null);
    Task<string> DeleteAsync(string id, CancellationToken ct = default, string? companyId = null);

    IQueryable<TDocument> GetDocuments(bool includeDeleted = false, string? companyId = null);
    IQueryable<TDocument> GetDocument(string id, bool includeDeleted = false, string? companyId = null);
    IQueryable<TDocument> GetDocuments(
        Expression<Func<TDocument, bool>> predicate, bool includeDeleted = false, string? companyId = null);

    IExecutable<TResult> AsExecutable<TResult>(
        Expression<Func<TDocument, TResult>> selector,
        Expression<Func<TDocument, bool>>? predicate = null,
        bool includeDeleted = false);
}
