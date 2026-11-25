using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Events;

namespace ZeroNovaRealEstate.Application.Features.Common;

/// <summary>
/// Who a bell goes to: <see cref="UserIds"/> by id, or — when there is nobody to name — the holders of
/// <see cref="PermissionCodes"/>, resolved by zeronova-utility through zeronova-core.
/// </summary>
public sealed record BellRecipients(IReadOnlyList<string> UserIds, IReadOnlyList<string>? PermissionCodes)
{
    public bool IsEmpty => UserIds.Count == 0 && PermissionCodes is not { Count: > 0 };

    public static readonly BellRecipients Nobody = new([], null);

    public static BellRecipients User(string userId) => new([userId], null);

    /// <summary>
    /// The person the item belongs to (an agent, an uploader) unless they caused the event; when it has nobody, the holders
    /// of <paramref name="codes"/>. The hub cannot leave the actor out of a permission fan-out (a manager who acts may hear
    /// of their own change); when the owner is the actor and <paramref name="elseWhenActor"/> is false, nobody.
    /// </summary>
    public static BellRecipients OwnerElse(string? owner, string? actor, bool elseWhenActor, params string[] codes)
    {
        if (!string.IsNullOrWhiteSpace(owner))
        {
            if (owner != actor)
                return User(owner);
            if (!elseWhenActor)
                return Nobody;
        }
        return new([], codes);
    }
}

/// <summary>
/// The staff bells this service rings through zeronova-utility's dispatch (<c>dispatch-event</c>, N2 contract): a bell and
/// a toast for the named people, browser push when they are away. <c>Link</c> (= <c>Url</c>) is the admin app's route.
/// </summary>
public static class EstateBells
{
    public const string ProducedBy = "zeronova-real-estate";

    public static DispatchEventMessage Build(string type, string companyId, BellRecipients to, string title, string? body, string link, string icon,
        string correlation, DateTimeOffset now, string category = BellCategories.RealEstate, string? groupKey = null, object? data = null,
        string metaPriority = "normal") =>
        new(Guid.NewGuid().ToString(), type, companyId, string.Empty, data ?? new { url = link },
            new DispatchDelivery(Realtime: false, Persist: true, Push: "IfOffline"),
            new DispatchMeta(metaPriority, ProducedBy, correlation, now),
            new DispatchNotification(companyId, to.UserIds, title, body, link, icon,
                Link: link, Category: category, Priority: BellCategories.Normal, GroupKey: groupKey, Product: Products.RealEstate,
                Audience: to.PermissionCodes is { Count: > 0 } codes ? new DispatchAudience([], codes) : null));
}
