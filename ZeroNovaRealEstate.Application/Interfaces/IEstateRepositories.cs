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

    /// <summary>Stamps every unit of a project confirmed at <paramref name="at"/> (a partner project checked as a whole, D21).</summary>
    Task StampProjectConfirmedAsync(string projectId, DateTime at, CancellationToken ct = default);
}

public interface IProjectRepository : ICompanyRepositoryBase<ProjectDocument>;

public interface IUnitTypeRepository : ICompanyRepositoryBase<UnitTypeDocument>;

public interface IPriceListRepository : ICompanyRepositoryBase<PriceListDocument>;

public interface IPaymentPlanRepository : ICompanyRepositoryBase<PaymentPlanDocument>;

/// <summary>A row that belongs to a lead and may learn the lead's contact (C2).</summary>
public interface IContactBackfill
{
    /// <summary>Sets <paramref name="contactId"/> on the lead's rows that have none (idempotent). How many changed.</summary>
    Task<long> BackfillContactAsync(string leadId, string contactId, CancellationToken ct = default, string? companyId = null);
}

public interface IPartyRepository : ICompanyRepositoryBase<PartyDocument>, IContactBackfill;

public interface IRequirementRepository : ICompanyRepositoryBase<RequirementDocument>, IContactBackfill
{
    /// <summary>Watched requirements (D33) whose watch ended before <paramref name="now"/>, across companies — the watch sweep's read.</summary>
    Task<IReadOnlyList<RequirementDocument>> WatchesEndedAsync(DateTime now, int take, CancellationToken ct = default);
}

/// <summary>Website searches that found nothing (D34), counted per day and criteria.</summary>
public interface IDemandSignalRepository : ICompanyRepositoryBase<DemandSignalDocument>
{
    /// <summary>Adds one to the (day, criteria) row of the company, making it the first time (one atomic upsert).</summary>
    Task RecordAsync(DemandSignalDocument signal, string companyId, CancellationToken ct = default);
}

/// <summary>The Customers list's summaries (C2): one per customer key, replaced whole on every refresh.</summary>
public interface ICustomerRepository : ICompanyRepositoryBase<CustomerDocument>
{
    /// <summary>Writes the summary for its <see cref="CustomerDocument.Key"/>, keeping the stored id.</summary>
    Task UpsertAsync(CustomerDocument customer, CancellationToken ct = default, string? companyId = null);

    /// <summary>Removes the summaries with these keys (a lead's row merged into its contact's, or nothing left).</summary>
    Task RemoveKeysAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default, string? companyId = null);
}

public interface IMatchRepository : ICompanyRepositoryBase<MatchDocument>
{
    /// <summary>
    /// Writes a new match, or refreshes the score of one still new; a match already shown, interested or rejected is
    /// left alone. True when the pair was new.
    /// </summary>
    Task<bool> UpsertNewAsync(MatchDocument match, CancellationToken ct = default, string? companyId = null);

    /// <summary>Drops the still-new matches of a property (or a requirement) not in <paramref name="keep"/>.</summary>
    Task DropNewAsync(string? propertyId, string? requirementId, IReadOnlyCollection<string> keep, CancellationToken ct = default, string? companyId = null);

    /// <summary>Removes a still-new match for good (a unit type's match with no unit left, D24).</summary>
    Task RemoveNewAsync(string matchId, CancellationToken ct = default, string? companyId = null);
}

public interface IVisitRepository : ICompanyRepositoryBase<VisitDocument>, IContactBackfill
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

    /// <summary>
    /// Counts an enquiry from the share page unless <paramref name="perHour"/> came in the hour before <paramref name="at"/>
    /// (one atomic update: the guard and the count cannot race). False = refused.
    /// </summary>
    Task<bool> TryRecordEnquiryAsync(string shareId, string companyId, DateTime at, int perHour, CancellationToken ct = default);
}

public interface IEnquiryRepository : ICompanyRepositoryBase<EnquiryDocument>
{
    /// <summary>
    /// Writes the enquiry unless one with its <see cref="EnquiryDocument.Ref"/> is there already (a redelivery, or a
    /// parallel delivery of the same event). True when written.
    /// </summary>
    Task<bool> AddIfNewAsync(EnquiryDocument enquiry, CancellationToken ct = default, string? companyId = null);
}

public interface IDealRepository : ICompanyRepositoryBase<DealDocument>;

public interface IAgreementTemplateRepository : ICompanyRepositoryBase<AgreementTemplateDocument>;

public interface IBrokerageRepository : ICompanyRepositoryBase<BrokerageDocument>;

public interface ITenancyRepository : ICompanyRepositoryBase<TenancyDocument>
{
    /// <summary>Active tenancies across companies — the rent reminders' read.</summary>
    Task<IReadOnlyList<TenancyDocument>> ActiveAnyTenantAsync(int skip, int take, CancellationToken ct = default);
}

public interface IEstateSettingsRepository : ICompanyRepositoryBase<EstateSettingsDocument>;

public interface IWebsiteRepository : ICompanyRepositoryBase<EstateWebsiteDocument>
{
    /// <summary>
    /// The website with this code, across companies — the public listings' one deliberate cross-tenant read (cms doc 06
    /// §12). The code is the only key; never use it for anything else.
    /// </summary>
    Task<EstateWebsiteDocument?> FindByCodeAnyTenantAsync(string code, CancellationToken ct = default);
}

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

/// <summary>Asks zeronova-lead to put a person into the dealer's contact book.</summary>
public interface IContactSyncProducer : IMessageProducer
{
    Task PublishAsync(ContactUpsertRequestedEvent evt, CancellationToken ct = default);
}

/// <summary>An enquiry for zeronova-lead to record on a lead (D29): the share page's form and "Add enquiry".</summary>
public interface ILeadCaptureProducer : IMessageProducer
{
    Task PublishAsync(LeadCapturedEnquiryEvent evt, string changedBy, CancellationToken ct = default);
}

/// <summary>A WhatsApp template for zeronova-social to send from the company's line (D33's one-click message).</summary>
public interface IWhatsAppTemplateProducer : IMessageProducer
{
    Task PublishAsync(WhatsAppFirstTouchRequestedEvent evt, CancellationToken ct = default);
}

/// <summary>A bell (and push when offline) for members, through zeronova-utility's dispatch.</summary>
public interface IAgentNotificationProducer : IMessageProducer
{
    Task PublishAsync(DispatchEventMessage evt, CancellationToken ct = default);
}

/// <summary>Property and project videos (D36), one row per zeronova-media video.</summary>
public interface IEstateVideoRepository : ICompanyRepositoryBase<EstateVideoDocument>;
