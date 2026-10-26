using Microsoft.Extensions.Logging;
using ZeroNova_Packages.Messaging.PubSub;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.PubSub;

/// <summary>Marker for this assembly, which the messaging setup scans for producers and consumers.</summary>
public sealed class PubSubAssembly;

/// <summary>A person, to zeronova-lead's <c>contact-requests</c> topic, through the outbox.</summary>
public sealed class ContactSyncProducer(ProducerServices services, ILogger<ContactSyncProducer> logger)
    : PubSubProducer<ContactSyncProducer>(Topics.ContactRequests.Topic, services, logger), IContactSyncProducer
{
    public Task PublishAsync(ContactUpsertRequestedEvent evt, CancellationToken ct = default) =>
        ProduceAsync(Topics.ContactRequests.UpsertRequested, evt, evt.ExternalRef, cancellationToken: ct);
}

/// <summary>A bell for an agent (new matches), to zeronova-utility's <c>communication-events</c>, through the outbox.</summary>
public sealed class AgentNotificationProducer(ProducerServices services, ILogger<AgentNotificationProducer> logger)
    : PubSubProducer<AgentNotificationProducer>(Topics.CommunicationEvents.Topic, services, logger), IAgentNotificationProducer
{
    public Task PublishAsync(DispatchEventMessage evt, CancellationToken ct = default) =>
        ProduceAsync(Topics.CommunicationEvents.DispatchEvent, evt, "real-estate", cancellationToken: ct);
}
