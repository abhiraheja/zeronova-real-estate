using MongoDB.Driver;
using ZeroNova_Packages.Messaging.Mongo;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Infrastructure.Repositories.Base;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Infrastructure.Repositories;

public sealed class PropertyRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<PropertyDocument>(database, context), IPropertyRepository
{
    // Deliberately across companies — see the interface. Holds are few and short.
    public async Task<IReadOnlyList<PropertyDocument>> HoldsEndedAsync(string today, int take, CancellationToken ct = default) =>
        await Collection.Find(p => p.Status == PropertyStatus.OnHold && !p.IsDeleted && p.HoldUntil != null && p.HoldUntil.CompareTo(today) < 0)
            .Limit(take).ToListAsync(ct);
}

public sealed class PartyRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<PartyDocument>(database, context), IPartyRepository;

public sealed class RequirementRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<RequirementDocument>(database, context), IRequirementRepository;

public sealed class MatchRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<MatchDocument>(database, context), IMatchRepository
{
    public async Task<bool> UpsertNewAsync(MatchDocument match, CancellationToken ct = default, string? companyId = null)
    {
        var company = ResolveCompanyId(companyId);
        var f = Builders<MatchDocument>.Filter;
        var pair = f.Where(m => m.CompanyId == company && m.PropertyId == match.PropertyId && m.RequirementId == match.RequirementId);
        var existing = await Collection.Find(pair).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            try
            {
                match.Id = GetNewId;
                await AddAsync(match, ct, company);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;   // made by a parallel save; that one stands
            }
        }
        if (existing.Status != MatchStatus.New)
            return false;
        await AmbientWrites.UpdateOneAsync(Collection, f.Where(m => m.Id == existing.Id && m.CompanyId == company),
            Builders<MatchDocument>.Update
                .Set(m => m.Score, match.Score)
                .Set(m => m.Reasons, match.Reasons)
                .Set(m => m.AgentId, match.AgentId)
                .Set(m => m.UpdatedAt, match.UpdatedAt)
                .Set(m => m.UpdatedBy, Stamp())
                .Inc(m => m.Version, 1), ct);
        return false;
    }

    public Task DropNewAsync(string? propertyId, string? requirementId, IReadOnlyCollection<string> keep, CancellationToken ct = default,
        string? companyId = null)
    {
        var company = ResolveCompanyId(companyId);
        var f = Builders<MatchDocument>.Filter;
        var filter = f.Where(m => m.CompanyId == company && m.Status == MatchStatus.New);
        if (propertyId is not null)
            filter &= f.Eq(m => m.PropertyId, propertyId) & f.Nin(m => m.RequirementId, keep);
        if (requirementId is not null)
            filter &= f.Eq(m => m.RequirementId, requirementId) & f.Nin(m => m.PropertyId, keep);
        return Collection.DeleteManyAsync(filter, ct);
    }
}

public sealed class VisitRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<VisitDocument>(database, context), IVisitRepository
{
    // Deliberately across companies — see the interface.
    public async Task<IReadOnlyList<VisitDocument>> DueForReminderAsync(DateTime until, int take, CancellationToken ct = default) =>
        await Collection.Find(v => v.Status == Models.Visits.VisitStatus.Scheduled && !v.Reminded && !v.IsDeleted && v.At <= until)
            .Limit(take).ToListAsync(ct);
}

public sealed class ShareRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<ShareDocument>(database, context), IShareRepository
{
    // Deliberately across companies — see the interface. The token is the only key.
    public async Task<ShareDocument?> FindByTokenAnyTenantAsync(string token, CancellationToken ct = default) =>
        await Collection.Find(s => s.Token == token && !s.IsDeleted).FirstOrDefaultAsync(ct);

    public Task RecordOpenAsync(string shareId, string companyId, DateTime at, CancellationToken ct = default) =>
        Collection.UpdateOneAsync(s => s.Id == shareId && s.CompanyId == companyId,
            Builders<ShareDocument>.Update.Inc(s => s.Opens, 1).Set(s => s.LastOpenedAt, at), cancellationToken: ct);
}

public sealed class DealRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<DealDocument>(database, context), IDealRepository;

public sealed class AgreementTemplateRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<AgreementTemplateDocument>(database, context), IAgreementTemplateRepository;

public sealed class EstateSettingsRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<EstateSettingsDocument>(database, context), IEstateSettingsRepository;

public sealed class CustomFieldRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<CustomFieldDocument>(database, context), ICustomFieldRepository;

public sealed class LocalityRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<LocalityDocument>(database, context), ILocalityRepository
{
    public async Task EnsureAsync(string name, string key, string? city, CancellationToken ct = default, string? companyId = null)
    {
        var company = ResolveCompanyId(companyId);
        if (await Collection.Find(l => l.CompanyId == company && l.Key == key && !l.IsDeleted).AnyAsync(ct))
            return;
        try
        {
            await AddAsync(new LocalityDocument { Id = GetNewId, Name = name, Key = key, City = city }, ct, company);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Typed by someone else at the same moment.
        }
    }
}

public sealed class CounterRepository(IMongoDatabase database, IRequestContext context)
    : CompanyRepositoryBase<CounterDocument>(database, context), ICounterRepository
{
    public async Task<long> NextAsync(string name, CancellationToken ct = default, string? companyId = null)
    {
        var company = ResolveCompanyId(companyId);
        var counter = await Collection.FindOneAndUpdateAsync(
            Builders<CounterDocument>.Filter.Where(c => c.CompanyId == company && c.Name == name),
            Builders<CounterDocument>.Update.Inc(c => c.Value, 1).SetOnInsert(c => c.Id, GetNewId).SetOnInsert(c => c.IsDeleted, false),
            new FindOneAndUpdateOptions<CounterDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
        return counter.Value;
    }
}
