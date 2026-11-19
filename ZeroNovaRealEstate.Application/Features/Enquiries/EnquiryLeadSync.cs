using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Enquiries;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Enquiries;

/// <summary>
/// The enquiry is the lead's: when one is worked here — its status moved (contacted, a requirement or visit linked) or it
/// was closed or re-closed with a reason or note — zeronova-lead hears of it on <c>lead-captured-enquiry-updated</c> and
/// puts it on the lead's timeline and notes.
/// <para>
/// D31 still holds where it protects something: an enquiry is one listing, the lead is the customer. The event says
/// whether the customer still has something open here (another open enquiry, or an active, on-hold or watched
/// requirement); while they do, zeronova-lead never closes the lead for an enquiry closed as not interested. Every other
/// close leaves the lead as it is.
/// </para>
/// <para>Best effort: a lead that never hears of a change must not fail the dealer's save.</para>
/// </summary>
public sealed class EnquiryLeadSync(IEnquiryRepository enquiries, IRequirementRepository requirements, ILeadEnquiryUpdateProducer producer,
    TimeProvider clock, ILogger<EnquiryLeadSync> logger)
{
    private static readonly RequirementStatus[] LiveRequirement = [RequirementStatus.Active, RequirementStatus.OnHold, RequirementStatus.Watching];

    /// <summary>What an enquiry was before a change — compared with what it is after.</summary>
    public sealed record Before(EnquiryStatus Status, EnquiryCloseReason? CloseReason, string? CloseNote, string? Wanted)
    {
        public static Before Of(EnquiryDocument e) => new(e.Status, e.CloseReason, e.CloseNote, e.Wanted);
    }

    /// <summary>Tells the enquiry's lead, when it has one and the change is one it should hear of.</summary>
    public async Task ChangedAsync(EnquiryDocument e, Before before, string? actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.LeadId)) return;
        if (!Matters(e, before)) return;
        try
        {
            var stillActive = e.Status == EnquiryStatus.Closed && await CustomerStillActiveAsync(e, ct);
            await producer.PublishAsync(new LeadEnquiryUpdatedEvent(
                e.CompanyId, e.LeadId, e.ContactId, e.Id, e.Code, e.Ref, SubjectOf(e), before.Status.ToString(), e.Status.ToString(),
                e.CloseReason?.ToString(), e.CloseNote, e.Wanted, e.RequirementId, e.VisitId, e.AgentId, e.AgentName, stillActive, actor,
                clock.GetUtcNow(), ObjectId.GenerateNewId().ToString()), actor ?? "real-estate", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Enquiry {EnquiryId}: its lead {LeadId} was not told of the change", e.Id, e.LeadId);
        }
    }

    /// <summary>A status change, or a closed enquiry's reason, note or wanted changed. A re-link, an agent change alone: no.</summary>
    public static bool Matters(EnquiryDocument e, Before before) =>
        e.Status != before.Status
        || (e.Status == EnquiryStatus.Closed && (e.CloseReason != before.CloseReason || e.CloseNote != before.CloseNote || e.Wanted != before.Wanted));

    private async Task<bool> CustomerStillActiveAsync(EnquiryDocument e, CancellationToken ct)
    {
        string lead = e.LeadId!, id = e.Id;
        if (await enquiries.GetDocuments(x => x.LeadId == lead && x.Id != id && x.Status != EnquiryStatus.Closed).AnyAsync(ct))
            return true;
        return await requirements.GetDocuments(r => r.LeadId == lead && LiveRequirement.Contains(r.Status)).AnyAsync(ct);
    }

    public static string? SubjectOf(EnquiryDocument e)
    {
        var listing = string.Join(" ", new[] { e.PropertyCode, e.PropertyTitle }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (listing.Length > 0) return listing;
        var project = string.Join(" · ", new[] { e.ProjectName, e.UnitTypeName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return project.Length > 0 ? project : null;
    }
}
