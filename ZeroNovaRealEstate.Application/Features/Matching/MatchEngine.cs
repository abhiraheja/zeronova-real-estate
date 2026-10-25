using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;

namespace ZeroNovaRealEstate.Application.Features.Matching;

/// <summary>The verdict for one property and one requirement: whether it fits at all, how well (0–100), and why.</summary>
public sealed record MatchVerdict(bool Fits, int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Matching both ways (D10, design 4.5). A property is tried against every active requirement of its purpose when it is
/// saved, re-confirmed or comes back to available; a requirement against the available stock when it is saved. Matches
/// at or above the dealer's threshold are kept; a new one that no longer fits is dropped; one already shown, interested
/// or rejected is history and never offered again. Each agent with new matches gets one bell.
/// <para>
/// Runs inside the save (a dealer's stock is thousands, not millions). The <c>companyId</c> overloads are for the hold
/// sweep, which runs with no caller.
/// </para>
/// </summary>
public sealed class MatchEngine(
    IPropertyRepository properties,
    IRequirementRepository requirements,
    IMatchRepository matches,
    IEstateSettingsRepository settings,
    IAgentNotificationProducer bell,
    TimeProvider clock,
    ILogger<MatchEngine> logger)
{
    /// <summary>Tries the property against the active requirements; answers how many new matches it made.</summary>
    public async Task<int> ForPropertyAsync(PropertyDocument property, CancellationToken ct, string? companyId = null)
    {
        var s = await EstateSettings.LoadAsync(settings, ct, companyId);
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await requirements
            .GetDocuments(r => r.Status == RequirementStatus.Active && r.Purpose == property.Purpose, false, companyId)
            .ToListAsync(ct);

        var kept = new List<string>();
        var created = new List<RequirementDocument>();
        foreach (var requirement in candidates)
        {
            var verdict = Score(property, requirement, s, now);
            if (!verdict.Fits || verdict.Score < s.MatchThreshold)
                continue;
            kept.Add(requirement.Id);
            if (await matches.UpsertNewAsync(NewMatch(property, requirement, verdict, now), ct, companyId))
                created.Add(requirement);
        }
        await matches.DropNewAsync(property.Id, null, kept, ct, companyId);

        foreach (var group in created.GroupBy(r => r.AgentId))
            await TellAsync(property.CompanyId, group.Key, group.Count() == 1
                    ? $"{property.Code} fits {group.First().LeadName ?? "a customer"}"
                    : $"{property.Code} fits {group.Count()} of your customers",
                property.Title, $"/real-estate/properties/{property.Id}", property.Id, ct);
        return created.Count;
    }

    /// <summary>Tries the requirement against the available stock; answers how many new matches it made.</summary>
    public async Task<int> ForRequirementAsync(RequirementDocument requirement, CancellationToken ct, string? companyId = null)
    {
        if (requirement.Status != RequirementStatus.Active)
        {
            await matches.DropNewAsync(null, requirement.Id, [], ct, companyId);
            return 0;
        }

        var s = await EstateSettings.LoadAsync(settings, ct, companyId);
        var now = clock.GetUtcNow().UtcDateTime;
        var query = properties.GetDocuments(p => p.Status == PropertyStatus.Available && p.Purpose == requirement.Purpose, false, companyId);
        if (requirement.Types.Count > 0)
            query = query.Where(p => requirement.Types.Contains(p.Type));
        if (requirement.LocalityKeys.Count > 0)
            query = query.Where(p => requirement.LocalityKeys.Contains(p.LocalityKey));
        if (requirement.BudgetMax is { } max)
        {
            var ceiling = max * (100 + s.BudgetTolerancePercent) / 100m;
            query = query.Where(p => p.PriceValue == null || p.PriceValue <= ceiling);
        }
        var candidates = await query.ToListAsync(ct);

        var kept = new List<string>();
        var created = 0;
        foreach (var property in candidates)
        {
            var verdict = Score(property, requirement, s, now);
            if (!verdict.Fits || verdict.Score < s.MatchThreshold)
                continue;
            kept.Add(property.Id);
            if (await matches.UpsertNewAsync(NewMatch(property, requirement, verdict, now), ct, companyId))
                created++;
        }
        await matches.DropNewAsync(null, requirement.Id, kept, ct, companyId);

        if (created > 0)
            await TellAsync(requirement.CompanyId, requirement.AgentId,
                created == 1 ? $"1 property fits {requirement.LeadName ?? "your customer"}" : $"{created} properties fit {requirement.LeadName ?? "your customer"}",
                null, $"/real-estate/requirements/{requirement.Id}", requirement.Id, ct);
        return created;
    }

    /// <summary>Drops the still-new matches of a property that left the stock (sold, withdrawn, deleted, stale).</summary>
    public Task DropForPropertyAsync(string propertyId, CancellationToken ct, string? companyId = null) =>
        matches.DropNewAsync(propertyId, null, [], ct, companyId);

    /// <summary>
    /// Hard filters, then a score. A missing fact the customer asked for (BHK, facing, size not filled) does not rule a
    /// property out — it costs points and is named, so the agent can ask.
    /// </summary>
    public static MatchVerdict Score(PropertyDocument p, RequirementDocument r, EstateSettingsDocument s, DateTime utcNow)
    {
        var no = new MatchVerdict(false, 0, []);
        if (p.Status != PropertyStatus.Available || p.IsDeleted || EstateSettings.IsStale(p, s, utcNow))
            return no;
        if (p.Purpose != r.Purpose)
            return no;
        if (r.Types.Count > 0 && !r.Types.Contains(p.Type))
            return no;
        if (r.City is { Length: > 0 } city && p.City is { Length: > 0 } pCity && !string.Equals(city.Trim(), pCity.Trim(), StringComparison.OrdinalIgnoreCase))
            return no;
        if (r.LocalityKeys.Count > 0 && !r.LocalityKeys.Contains(p.LocalityKey))
            return no;

        var score = 100;
        var reasons = new List<string>();

        // Locality: the customer's first choice scores highest.
        if (r.LocalityKeys.Count > 0)
        {
            var rank = r.LocalityKeys.IndexOf(p.LocalityKey);
            score -= Math.Min(rank * 5, 15);
            reasons.Add(rank == 0 ? $"{p.Locality} (first choice)" : $"{p.Locality} (choice {rank + 1})");
        }

        // Budget, with the dealer's tolerance over the maximum.
        if (r.BudgetMax is { } max)
        {
            if (p.PriceValue is not { } price)
            {
                score -= 10;
                reasons.Add("Price not filled");
            }
            else if (price > max * (100 + s.BudgetTolerancePercent) / 100m)
                return no;
            else if (price > max)
            {
                score -= 15;
                reasons.Add($"{Math.Ceiling((price - max) * 100m / max)}% over budget");
            }
            else if (r.BudgetMin is { } min && price < min)
            {
                score -= 5;
                reasons.Add("Below budget");
            }
            else
                reasons.Add("In budget");
        }

        // BHK (built property).
        if (r.Bhk.Count > 0)
        {
            if (p.Bhk is { } bhk)
            {
                if (!r.Bhk.Contains(bhk))
                    return no;
                reasons.Add($"{bhk} BHK");
            }
            else if (IsBuilt(p.Type))
            {
                score -= 10;
                reasons.Add("BHK not filled");
            }
        }

        // Size, in sq ft, a little either side.
        if (r.SizeMin is not null || r.SizeMax is not null)
        {
            if (p.SizeSqFt is { } size)
            {
                if (r.SizeMin is { } lo && size < lo.SqFt * 0.9m)
                    return no;
                if (r.SizeMax is { } hi && size > hi.SqFt * 1.2m)
                    return no;
                if ((r.SizeMin is { } lo2 && size < lo2.SqFt) || (r.SizeMax is { } hi2 && size > hi2.SqFt))
                {
                    score -= 10;
                    reasons.Add($"Size {size:#,##0} sq ft (near the range)");
                }
                else
                    reasons.Add($"Size {size:#,##0} sq ft");
            }
            else
            {
                score -= 10;
                reasons.Add("Size not filled");
            }
        }

        if (r.CornerOnly)
        {
            if (!p.Corner)
                return no;
            reasons.Add("Corner");
        }
        if (r.ParkFacingOnly)
        {
            if (!p.ParkFacing)
                return no;
            reasons.Add("Park facing");
        }
        if (r.Facings.Count > 0)
        {
            if (p.Facing is { } facing)
            {
                if (!r.Facings.Contains(facing))
                    return no;
                reasons.Add($"{facing} facing");
            }
            else
            {
                score -= 10;
                reasons.Add("Facing not filled");
            }
        }

        if (r.NeedsLift && p.Lift == false)
            return no;
        if (r.NeedsParking && p.CoveredParking == 0 && p.OpenParking == 0)
            return no;

        switch (r.Floor)
        {
            case FloorPreference.Ground when p.Floor is { } f && f != 0:
            case FloorPreference.NotTop when p.Floor is { } top && p.TotalFloors is { } total && top >= total:
            case FloorPreference.Top when p.Floor is { } t && p.TotalFloors is { } all && t < all:
                return no;
        }

        if (r.Furnishings.Count > 0 && p.Furnishing is { } furnishing && !r.Furnishings.Contains(furnishing))
            return no;
        if (r.TenantType is { } tenant && p.TenantPreferences.Count > 0 && !p.TenantPreferences.Contains(tenant))
            return no;

        if (p.LastConfirmedAt < utcNow.AddDays(-7))
            score -= 5;

        return new MatchVerdict(true, Math.Clamp(score, 0, 100), reasons);
    }

    public static bool IsBuilt(PropertyType type) => type is not (PropertyType.Plot or PropertyType.AgriculturalLand or PropertyType.IndustrialPlot);

    private static MatchDocument NewMatch(PropertyDocument p, RequirementDocument r, MatchVerdict verdict, DateTime now) => new()
    {
        Id = string.Empty,
        PropertyId = p.Id,
        RequirementId = r.Id,
        AgentId = r.AgentId,
        Score = verdict.Score,
        Reasons = [.. verdict.Reasons],
        PriceWhenMatched = p.PriceValue,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>One bell for an agent. A lost bell is logged, never a failed save — the matches are already written.</summary>
    private async Task TellAsync(string companyId, string agentId, string title, string? body, string url, string correlation, CancellationToken ct)
    {
        try
        {
            await bell.PublishAsync(new DispatchEventMessage(
                Guid.NewGuid().ToString(), "ESTATE_NEW_MATCHES", companyId, string.Empty, new { url },
                new DispatchDelivery(Realtime: false, Persist: true, Push: "IfOffline"),
                new DispatchMeta("normal", "zeronova-real-estate", correlation, clock.GetUtcNow()),
                new DispatchNotification(companyId, [agentId], title, body, url, "building")), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "New-match bell for agent {AgentId} of company {CompanyId} not sent.", agentId, companyId);
        }
    }
}
