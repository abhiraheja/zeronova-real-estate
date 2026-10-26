using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Application.Features.Parties;

/// <summary>
/// Puts a person into the dealer's contact book in zeronova-lead, tagged by what they are here: every saved person
/// "Property party", then "Property owner", "Seller" / "Buyer" or "Landlord" / "Tenant" as they take that place.
/// zeronova-lead adds the tags to a contact it already has. Only name, phone and email leave this service — never
/// Aadhaar, PAN or the address. A failure is logged and never fails the save. A completed deal also closes the
/// buyers' open enquiries as won (<see cref="WinAsync"/>).
/// </summary>
public sealed class PartyContacts(IPartyRepository parties, IContactSyncProducer producer, ILogger<PartyContacts> logger)
{
    public const string Party = "Property party";
    public const string Owner = "Property owner";
    public const string Seller = "Seller";
    public const string Buyer = "Buyer";
    public const string Landlord = "Landlord";
    public const string Tenant = "Tenant";

    public Task SyncAsync(PartyDocument party, CancellationToken ct, params string[] tags) => PublishAsync(party, tags, null, null, ct);

    /// <summary>
    /// The buyers (or tenants) of a completed deal, with <paramref name="reason"/>: zeronova-lead closes each one's open
    /// lead as won — the deal's own <paramref name="leadId"/> goes with the first buyer, whose enquiry it is.
    /// </summary>
    public async Task WinAsync(IReadOnlyList<string> partyIds, string[] tags, string reason, string? leadId, CancellationToken ct)
    {
        var people = (await parties.GetDocuments(p => partyIds.Contains(p.Id)).ToListAsync(ct))
            .OrderBy(p => partyIds.ToList().IndexOf(p.Id)).ToList();
        for (var i = 0; i < people.Count; i++)
            await PublishAsync(people[i], tags, reason, i == 0 ? leadId : null, ct);
    }

    private async Task PublishAsync(PartyDocument party, string[] tags, string? wonReason, string? leadId, CancellationToken ct)
    {
        var phone = party.Phone ?? party.WhatsApp;
        if (phone is null && party.Email is null)
            return;
        try
        {
            await producer.PublishAsync(new ContactUpsertRequestedEvent(
                party.CompanyId, party.Name, phone, party.Email, [Party, .. tags], $"party:{party.Id}", wonReason, leadId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Party {PartyId} was saved but not sent to the contact book.", party.Id);
        }
    }

    public async Task SyncAsync(IReadOnlyCollection<string> partyIds, string tag, CancellationToken ct)
    {
        if (partyIds.Count == 0)
            return;
        foreach (var party in await parties.GetDocuments(p => partyIds.Contains(p.Id)).ToListAsync(ct))
            await SyncAsync(party, ct, tag);
    }
}
