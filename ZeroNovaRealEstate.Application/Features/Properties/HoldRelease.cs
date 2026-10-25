using Microsoft.Extensions.Logging;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Properties;

/// <summary>
/// A hold that ran out (D6): the property is available again and is matched again. Runs with no caller, across
/// companies, through the repositories' <c>companyId</c> overloads.
/// </summary>
public sealed class HoldRelease(IPropertyRepository properties, MatchEngine engine, TimeProvider clock, ILogger<HoldRelease> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var ended = await properties.HoldsEndedAsync(EstateText.Today(clock), 100, ct);
        foreach (var property in ended)
        {
            property.Status = PropertyStatus.Available;
            property.HoldUntil = null;
            await properties.UpdateAsync(property, ct, property.CompanyId);
            await engine.ForPropertyAsync(property, ct, property.CompanyId);
            logger.LogInformation("Hold on {Code} of company {CompanyId} ended; available again.", property.Code, property.CompanyId);
        }
        return ended.Count;
    }
}
