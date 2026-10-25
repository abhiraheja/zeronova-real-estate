using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Common;

/// <summary>
/// What the caller may see (D14, D15): owners' details with POWV; every requirement and match with PRQV, otherwise only
/// the ones they are the agent of. The admin role's <c>*</c> grants everything.
/// </summary>
public sealed class EstateScope(IRequestContext context)
{
    public bool Has(string code) => context.Permissions.Contains(code) || context.Permissions.Contains(EstatePermissions.Wildcard);

    public bool SeesOwners => Has(EstatePermissions.OwnerView);

    public bool SeesAllRequirements => Has(EstatePermissions.RequirementView);

    public bool SeesAllDeals => Has(EstatePermissions.DealView);

    public bool SeesAllRentals => Has(EstatePermissions.RentalView);

    /// <summary>The caller's ZeroNova user id (the agent id on requirements and matches).</summary>
    public string UserId => context.UserId ?? context.AuthUserId ?? throw new ValidatorException("Your account is not set up yet. Sign in again.");

    public bool MayWork(RequirementDocument requirement) => SeesAllRequirements || requirement.AgentId == UserId;

    public void RequireWork(RequirementDocument requirement)
    {
        if (!MayWork(requirement))
            throw new ValidatorException("This customer is another agent's.");
    }
}

/// <summary>Text, phones and dates as this service stores them.</summary>
public static partial class EstateText
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : Spaces().Replace(value.Trim(), " ");

    /// <summary>A locality's comparison key: lower case, single spaces ("Sector 70" and "sector  70" are one).</summary>
    public static string Key(string value) => Spaces().Replace(value.Trim().ToLowerInvariant(), " ");

    /// <summary>Digits with the country code: a 10-digit Indian number gets 91 in front.</summary>
    public static string? Phone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return null;
        if (digits.Length == 11 && digits[0] == '0')
            digits = digits[1..];
        if (digits.Length == 10)
            digits = "91" + digits;
        if (digits.Length is < 10 or > 15)
            throw new ValidatorException("Enter a phone number with 10 to 15 digits.");
        return digits;
    }

    /// <summary>A yyyy-MM-dd date, or null; anything else is refused.</summary>
    public static string? Date(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new ValidatorException($"{field} must be a date (yyyy-MM-dd).");
    }

    /// <summary>Today in India, yyyy-MM-dd.</summary>
    public static string Today(TimeProvider clock) =>
        clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>₹1,25,00,000 in the Indian grouping, as dealers write it.</summary>
    public static string Rupees(decimal amount)
    {
        var whole = (long)Math.Truncate(amount);
        var paise = Math.Round(Math.Abs(amount - whole) * 100m, 0);
        return "₹" + IndianWords.Group(whole) + (paise > 0 ? "." + paise.ToString("00", CultureInfo.InvariantCulture) : string.Empty);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>Area units to sq ft (D5): fixed factors, and the dealer's local value for the regional units.</summary>
public static class AreaMath
{
    public static decimal SqFtPer(AreaUnit unit, EstateSettingsDocument s) => unit switch
    {
        AreaUnit.SqFt => 1m,
        AreaUnit.SqYd => 9m,
        AreaUnit.SqM => 10.7639m,
        AreaUnit.Acre => 43560m,
        AreaUnit.Guntha => 1089m,
        AreaUnit.Cent => 435.6m,
        AreaUnit.Hectare => 107639.1m,
        AreaUnit.Marla => s.MarlaSqFt,
        AreaUnit.Kanal => s.KanalSqFt,
        AreaUnit.Bigha => s.BighaSqFt,
        AreaUnit.Biswa => s.BiswaSqFt,
        AreaUnit.Katha => s.KathaSqFt,
        _ => 1m,
    };

    public static AreaModel? Model(AreaInput? input, EstateSettingsDocument s)
    {
        if (input is null)
            return null;
        if (input.Value <= 0)
            throw new ValidatorException("A size must be more than zero.");
        return new AreaModel { Value = input.Value, Unit = input.Unit, SqFt = Math.Round(input.Value * SqFtPer(input.Unit, s), 2) };
    }

    public static AreaDto? Dto(AreaModel? model) => model is null ? null : new AreaDto(model.Value, model.Unit, model.SqFt);

    /// <summary>"200 sq yd", "10 marla", "1,800 sq ft".</summary>
    public static string Label(AreaModel area) => area.Unit switch
    {
        AreaUnit.SqFt => $"{area.Value:#,##0.##} sq ft",
        AreaUnit.SqYd => $"{area.Value:#,##0.##} sq yd",
        AreaUnit.SqM => $"{area.Value:#,##0.##} sq m",
        _ => $"{area.Value:#,##0.##} {area.Unit.ToString().ToLowerInvariant()}",
    };
}

/// <summary>The dealer's settings, or the defaults when none were saved.</summary>
public static class EstateSettings
{
    public static async Task<EstateSettingsDocument> LoadAsync(IEstateSettingsRepository settings, CancellationToken ct, string? companyId = null) =>
        await settings.GetDocuments(false, companyId).FirstOrDefaultAsync(ct) ?? new EstateSettingsDocument { Id = string.Empty };

    /// <summary>Available or on hold, and not confirmed within the dealer's days for its purpose (D6).</summary>
    public static bool IsStale(PropertyDocument p, EstateSettingsDocument s, DateTime utcNow) =>
        p.Status is PropertyStatus.Available or PropertyStatus.OnHold
        && p.LastConfirmedAt < utcNow.AddDays(-(p.Purpose == PropertyPurpose.Rent ? s.RentFreshDays : s.SaleFreshDays));
}
