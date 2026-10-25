using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ZeroNovaRealEstate.Domain.Documents;

namespace ZeroNovaRealEstate.Infrastructure.Persistence;

/// <summary>
/// Creates the indexes on startup. Idempotent (same spec = no-op). A failure is logged, not fatal: the service answers
/// without an index, only slower — except the unique ones (a match per pair, a locality per key, a code per company),
/// which are what refuse a duplicate. Partial index filters take equality only (<c>Eq</c>, never <c>$ne</c>).
/// </summary>
public sealed class IndexInitializer(IMongoDatabase database, ILogger<IndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var properties = database.GetCollection<PropertyDocument>("re_properties");
            await properties.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PropertyDocument>(Builders<PropertyDocument>.IndexKeys.Ascending(p => p.CompanyId).Ascending(p => p.Code),
                    new CreateIndexOptions { Name = "company_code_unique", Unique = true }),
                new CreateIndexModel<PropertyDocument>(Builders<PropertyDocument>.IndexKeys
                    .Ascending(p => p.CompanyId).Ascending(p => p.Purpose).Ascending(p => p.Status).Ascending(p => p.LocalityKey),
                    new CreateIndexOptions { Name = "company_purpose_status_locality" }),
                new CreateIndexModel<PropertyDocument>(Builders<PropertyDocument>.IndexKeys.Ascending(p => p.CompanyId).Ascending(p => p.LastConfirmedAt),
                    new CreateIndexOptions { Name = "company_last_confirmed" }),
                new CreateIndexModel<PropertyDocument>(Builders<PropertyDocument>.IndexKeys.Ascending(p => p.CompanyId).Ascending(p => p.OwnerPhones),
                    new CreateIndexOptions { Name = "company_owner_phones" }),
                new CreateIndexModel<PropertyDocument>(Builders<PropertyDocument>.IndexKeys.Ascending(p => p.Status).Ascending(p => p.HoldUntil),
                    new CreateIndexOptions { Name = "status_hold_until" }),
            ], ct);

            var parties = database.GetCollection<PartyDocument>("re_parties");
            await parties.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PartyDocument>(Builders<PartyDocument>.IndexKeys.Ascending(p => p.CompanyId).Ascending(p => p.Phone),
                    new CreateIndexOptions { Name = "company_phone" }),
                new CreateIndexModel<PartyDocument>(Builders<PartyDocument>.IndexKeys.Ascending(p => p.CompanyId).Ascending(p => p.LeadId),
                    new CreateIndexOptions { Name = "company_lead" }),
            ], ct);

            var requirements = database.GetCollection<RequirementDocument>("re_requirements");
            await requirements.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RequirementDocument>(Builders<RequirementDocument>.IndexKeys
                    .Ascending(r => r.CompanyId).Ascending(r => r.Status).Ascending(r => r.Purpose),
                    new CreateIndexOptions { Name = "company_status_purpose" }),
                new CreateIndexModel<RequirementDocument>(Builders<RequirementDocument>.IndexKeys.Ascending(r => r.CompanyId).Ascending(r => r.LeadId),
                    new CreateIndexOptions { Name = "company_lead" }),
                new CreateIndexModel<RequirementDocument>(Builders<RequirementDocument>.IndexKeys.Ascending(r => r.CompanyId).Ascending(r => r.AgentId),
                    new CreateIndexOptions { Name = "company_agent" }),
            ], ct);

            // One match per property and requirement: a parallel save cannot make two.
            var matches = database.GetCollection<MatchDocument>("re_matches");
            await matches.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<MatchDocument>(Builders<MatchDocument>.IndexKeys
                    .Ascending(m => m.CompanyId).Ascending(m => m.PropertyId).Ascending(m => m.RequirementId),
                    new CreateIndexOptions { Name = "company_pair_unique", Unique = true }),
                new CreateIndexModel<MatchDocument>(Builders<MatchDocument>.IndexKeys.Ascending(m => m.CompanyId).Ascending(m => m.RequirementId),
                    new CreateIndexOptions { Name = "company_requirement" }),
                new CreateIndexModel<MatchDocument>(Builders<MatchDocument>.IndexKeys
                    .Ascending(m => m.CompanyId).Ascending(m => m.AgentId).Ascending(m => m.Status).Descending(m => m.CreatedAt),
                    new CreateIndexOptions { Name = "company_agent_status_created" }),
            ], ct);

            var visits = database.GetCollection<VisitDocument>("re_visits");
            await visits.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<VisitDocument>(Builders<VisitDocument>.IndexKeys.Ascending(v => v.CompanyId).Ascending(v => v.At),
                    new CreateIndexOptions { Name = "company_at" }),
                new CreateIndexModel<VisitDocument>(Builders<VisitDocument>.IndexKeys.Ascending(v => v.Status).Ascending(v => v.Reminded).Ascending(v => v.At),
                    new CreateIndexOptions { Name = "status_reminded_at" }),
            ], ct);

            // A share token is unguessable and unique across companies (the public page finds it by token alone).
            var shares = database.GetCollection<ShareDocument>("re_shares");
            await shares.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ShareDocument>(Builders<ShareDocument>.IndexKeys.Ascending(x => x.Token),
                    new CreateIndexOptions { Name = "token_unique", Unique = true }),
                new CreateIndexModel<ShareDocument>(Builders<ShareDocument>.IndexKeys.Ascending(x => x.CompanyId).Ascending(x => x.PropertyId),
                    new CreateIndexOptions { Name = "company_property" }),
            ], ct);

            var deals = database.GetCollection<DealDocument>("re_deals");
            await deals.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<DealDocument>(Builders<DealDocument>.IndexKeys.Ascending(d => d.CompanyId).Ascending(d => d.Code),
                    new CreateIndexOptions { Name = "company_code_unique", Unique = true }),
                new CreateIndexModel<DealDocument>(Builders<DealDocument>.IndexKeys.Ascending(d => d.CompanyId).Ascending(d => d.PropertyId),
                    new CreateIndexOptions { Name = "company_property" }),
                new CreateIndexModel<DealDocument>(Builders<DealDocument>.IndexKeys.Ascending(d => d.CompanyId).Ascending(d => d.Stage),
                    new CreateIndexOptions { Name = "company_stage" }),
            ], ct);

            var brokerages = database.GetCollection<BrokerageDocument>("re_brokerages");
            await brokerages.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<BrokerageDocument>(Builders<BrokerageDocument>.IndexKeys.Ascending(b => b.CompanyId).Ascending(b => b.InvoiceNo),
                    new CreateIndexOptions { Name = "company_invoice_unique", Unique = true }),
                new CreateIndexModel<BrokerageDocument>(Builders<BrokerageDocument>.IndexKeys.Ascending(b => b.CompanyId).Ascending(b => b.DealId),
                    new CreateIndexOptions { Name = "company_deal" }),
            ], ct);
            var tenancies = database.GetCollection<TenancyDocument>("re_tenancies");
            await tenancies.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<TenancyDocument>(Builders<TenancyDocument>.IndexKeys.Ascending(t => t.CompanyId).Ascending(t => t.Status).Ascending(t => t.EndDate),
                    new CreateIndexOptions { Name = "company_status_end" }),
                new CreateIndexModel<TenancyDocument>(Builders<TenancyDocument>.IndexKeys.Ascending(t => t.Status),
                    new CreateIndexOptions { Name = "status" }),
            ], ct);

            var localities = database.GetCollection<LocalityDocument>("re_localities");
            await localities.Indexes.CreateOneAsync(new CreateIndexModel<LocalityDocument>(
                Builders<LocalityDocument>.IndexKeys.Ascending(l => l.CompanyId).Ascending(l => l.Key),
                new CreateIndexOptions<LocalityDocument>
                {
                    Name = "company_key_unique",
                    Unique = true,
                    PartialFilterExpression = Builders<LocalityDocument>.Filter.Eq(l => l.IsDeleted, false),
                }), cancellationToken: ct);

            var fields = database.GetCollection<CustomFieldDocument>("re_custom_fields");
            await fields.Indexes.CreateOneAsync(new CreateIndexModel<CustomFieldDocument>(
                Builders<CustomFieldDocument>.IndexKeys.Ascending(f => f.CompanyId).Ascending(f => f.Key),
                new CreateIndexOptions<CustomFieldDocument>
                {
                    Name = "company_key_unique",
                    Unique = true,
                    PartialFilterExpression = Builders<CustomFieldDocument>.Filter.Eq(f => f.IsDeleted, false),
                }), cancellationToken: ct);

            var settings = database.GetCollection<EstateSettingsDocument>("re_settings");
            await settings.Indexes.CreateOneAsync(new CreateIndexModel<EstateSettingsDocument>(
                Builders<EstateSettingsDocument>.IndexKeys.Ascending(s => s.CompanyId), new CreateIndexOptions { Name = "company_unique", Unique = true }),
                cancellationToken: ct);

            var counters = database.GetCollection<CounterDocument>("re_counters");
            await counters.Indexes.CreateOneAsync(new CreateIndexModel<CounterDocument>(
                Builders<CounterDocument>.IndexKeys.Ascending(c => c.CompanyId).Ascending(c => c.Name),
                new CreateIndexOptions { Name = "company_name_unique", Unique = true }), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Creating the Real Estate indexes failed; the service runs without them.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
