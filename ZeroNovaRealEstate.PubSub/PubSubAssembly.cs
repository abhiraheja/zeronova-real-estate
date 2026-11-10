using Microsoft.Extensions.Logging;
using ZeroNova_Packages.Messaging.PubSub;
using ZeroNovaRealEstate.Application.Features.Enquiries;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Videos;
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

/// <summary>An enquiry for zeronova-lead (share page, "Add enquiry"), to its <c>lead-captured</c> topic, through the outbox.</summary>
public sealed class LeadCaptureProducer(ProducerServices services, ILogger<LeadCaptureProducer> logger)
    : PubSubProducer<LeadCaptureProducer>(Topics.LeadCaptured.Topic, services, logger), ILeadCaptureProducer
{
    public Task PublishAsync(LeadCapturedEnquiryEvent evt, string changedBy, CancellationToken ct = default) =>
        ProduceAsync(Topics.LeadCaptured.Enquiry, evt, changedBy, cancellationToken: ct);
}

/// <summary>A WhatsApp template to a customer (D33), to zeronova-social's <c>whatsapp-sends</c>, through the outbox.</summary>
public sealed class WhatsAppTemplateProducer(ProducerServices services, ILogger<WhatsAppTemplateProducer> logger)
    : PubSubProducer<WhatsAppTemplateProducer>(Topics.WhatsAppSends.Topic, services, logger), IWhatsAppTemplateProducer
{
    public Task PublishAsync(WhatsAppFirstTouchRequestedEvent evt, CancellationToken ct = default) =>
        ProduceAsync(Topics.WhatsAppSends.FirstTouchRequested, evt, "real-estate", cancellationToken: ct);
}

// ── Consumers ────────────────────────────────────────────────────────────────────────────────────────────────────
// One class per (topic, subscription, event type); the notification and its handler live in Application
// (Features/Enquiries). The subscription is prefixed with Messaging:Source: zeronova-real-estate.lead-enquiry-sub.

/// <summary>Every enquiry zeronova-lead captured (D28); the handler keeps the Real Estate ones in <c>re_enquiries</c>.</summary>
public sealed class LeadEnquiryReceivedConsumer(ConsumerServices services, ILogger<LeadEnquiryReceivedConsumer> logger)
    : PubSubConsumer<LeadEnquiryReceivedConsumer, LeadEnquiryReceivedNotification>(Topics.LeadLifecycle.Topic, Topics.LeadLifecycle.EnquirySubscription,
        Topics.LeadLifecycle.EnquiryReceived, services, logger);

/// <summary>A WhatsApp send zeronova-social refused or failed (D33); the handler marks this service's own (key <c>re:…</c>) on its match.</summary>
public sealed class WhatsAppFailedConsumer(ConsumerServices services, ILogger<WhatsAppFailedConsumer> logger)
    : PubSubConsumer<WhatsAppFailedConsumer, WhatsAppSendFailedNotification>(Topics.WhatsAppLifecycle.Topic, Topics.WhatsAppLifecycle.FailedSubscription,
        Topics.WhatsAppLifecycle.Failed, services, logger);

/// <summary>A video zeronova-media made ready (D36); the handler marks this service's own (client <c>real-estate</c>) ready on its listing.</summary>
public sealed class VideoReadyConsumer(ConsumerServices services, ILogger<VideoReadyConsumer> logger)
    : PubSubConsumer<VideoReadyConsumer, VideoReadyNotification>(Topics.MediaEvents.Topic, Topics.MediaEvents.ReadySubscription,
        Topics.MediaEvents.VideoReady, services, logger);

/// <summary>A video zeronova-media could not make (too long, no picture, …); the handler marks this service's own failed with the reason.</summary>
public sealed class VideoFailedConsumer(ConsumerServices services, ILogger<VideoFailedConsumer> logger)
    : PubSubConsumer<VideoFailedConsumer, VideoFailedNotification>(Topics.MediaEvents.Topic, Topics.MediaEvents.FailedSubscription,
        Topics.MediaEvents.VideoFailed, services, logger);
