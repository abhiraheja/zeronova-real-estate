using ZeroNova_Packages.Messaging.Abstractions;
using ZeroNovaRealEstate.Application.Interfaces.Base;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Application.Interfaces;

public interface IPropertyRepository : ICompanyRepositoryBase<PropertyDocument>
{
    /// <summary>
    /// Every property on hold past its date, across companies — the hold sweep's one deliberate cross-tenant read.
    /// </summary>
    Task<IReadOnlyList<PropertyDocument>> HoldsEndedAsync(string today, int take, CancellationToken ct = default);
}

public interface IPartyRepository : ICompanyRepositoryBase<PartyDocument>;

public interface IRequirementRepository : ICompanyRepositoryBase<RequirementDocument>;

public interface IMatchRepository : ICompanyRepositoryBase<MatchDocument>
{
    /// <summary>
    /// Writes a new match, or refreshes the score of one still new; a match already shown, interested or rejected is
    /// left alone. True when the pair was new.
    /// </summary>
    Task<bool> UpsertNewAsync(MatchDocument match, CancellationToken ct = default, string? companyId = null);

    /// <summary>Drops the still-new matches of a property (or a requirement) not in <paramref name="keep"/>.</summary>
    Task DropNewAsync(string? propertyId, string? requirementId, IReadOnlyCollection<string> keep, CancellationToken ct = default, string? companyId = null);
}

public interface IVisitRepository : ICompanyRepositoryBase<VisitDocument>
{
    /// <summary>Scheduled visits starting before <paramref name="until"/> and not yet reminded, across companies — the reminder job's read.</summary>
    Task<IReadOnlyList<VisitDocument>> DueForReminderAsync(DateTime until, int take, CancellationToken ct = default);
}

public interface IShareRepository : ICompanyRepositoryBase<ShareDocument>
{
    /// <summary>The share with this token, across companies — the public page's one deliberate cross-tenant read.</summary>
    Task<ShareDocument?> FindByTokenAnyTenantAsync(string token, CancellationToken ct = default);

    /// <summary>Counts an open.</summary>
    Task RecordOpenAsync(string shareId, string companyId, DateTime at, CancellationToken ct = default);
}

public interface IEstateSettingsRepository : ICompanyRepositoryBase<EstateSettingsDocument>;

public interface ICustomFieldRepository : ICompanyRepositoryBase<CustomFieldDocument>;

public interface ILocalityRepository : ICompanyRepositoryBase<LocalityDocument>
{
    /// <summary>Records the name once per company (by its key); a second use changes nothing.</summary>
    Task EnsureAsync(string name, string key, string? city, CancellationToken ct = default, string? companyId = null);
}

public interface ICounterRepository : ICompanyRepositoryBase<CounterDocument>
{
    Task<long> NextAsync(string name, CancellationToken ct = default, string? companyId = null);
}

/// <summary>A bell (and push when offline) for members, through zeronova-utility's dispatch.</summary>
public interface IAgentNotificationProducer : IMessageProducer
{
    Task PublishAsync(DispatchEventMessage evt, CancellationToken ct = default);
}
