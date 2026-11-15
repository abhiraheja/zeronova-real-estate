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

    /// <summary>Deals at all (PDLV or PDLM): every one with PDLV, one's own with PDLM only.</summary>
    public bool MayDeals => SeesAllDeals || Has(EstatePermissions.DealManage);

    /// <summary>Tenancies at all (PRNV or PRNM): every one with PRNV, one's own with PRNM only.</summary>
    public bool MayRentals => SeesAllRentals || Has(EstatePermissions.RentalManage);

    /// <summary>The caller's id, or null with no caller (a job).</summary>
    public string? UserIdOrNull => context.UserId ?? context.AuthUserId;

    /// <summary>The caller's ZeroNova user id (the agent id on requirements and matches).</summary>
    public string UserId => context.UserId ?? context.AuthUserId ?? throw new ValidatorException("Your account is not set up yet. Sign in again.");

    public bool MayWork(RequirementDocument requirement) => SeesAllRequirements || requirement.AgentId == UserId;

    public void RequireWork(RequirementDocument requirement)
    {
        if (!MayWork(requirement))
            throw new ValidatorException("This customer is another agent's.");
    }

    /// <summary>Enquiries follow requirements (D39): one's own (its agent is the caller), or everyone's with PRQV.</summary>
    public bool MayWork(EnquiryDocument enquiry) => SeesAllRequirements || enquiry.AgentId == UserId;

    public void RequireWork(EnquiryDocument enquiry)
    {
        if (!MayWork(enquiry))
            throw new ValidatorException("This enquiry is another agent's.");
    }
}

/// <summary>
/// India time (Asia/Kolkata: UTC+5:30 all year, no daylight saving) for day, week and month boundaries — weeks start on
/// Monday.
/// </summary>
public static class IndiaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    public static DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc) + Offset);

    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow().UtcDateTime);

    /// <summary>The instant (UTC) the India day begins.</summary>
    public static DateTime StartUtc(DateOnly day) => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    public static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
    /// <summary>
    /// A unit land is measured in (marla, kanal, gaj = sq yd, bigha, acre …), not a floor area's sq ft or sq m. A size
    /// in one of these is the land: it leads a title and is compared with the plot area.
    /// </summary>
    public static bool IsLandUnit(AreaUnit unit) => unit is not (AreaUnit.SqFt or AreaUnit.SqM);

    /// <summary>A title's size, as a dealer says it: "5 Marla", "1 Kanal", "200 Gaj", "2 Acre", "1,800 Sq Ft".</summary>
    public static string TitleLabel(AreaModel area) => area.Unit switch
    {
        AreaUnit.SqFt => $"{area.Value:#,##0.##} Sq Ft",
        AreaUnit.SqYd => $"{area.Value:#,##0.##} Gaj",
        AreaUnit.SqM => $"{area.Value:#,##0.##} Sq M",
        _ => $"{area.Value:#,##0.##} {area.Unit}",
    };

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

    /// <summary>
    /// Available or on hold, and not confirmed within the dealer's days for its purpose (D6). A project unit is never
    /// stale on its own (D21): an own project is always fresh, a partner project is confirmed as a whole.
    /// </summary>
    public static bool IsStale(PropertyDocument p, EstateSettingsDocument s, DateTime utcNow) =>
        p.ProjectId is null
        && p.Status is PropertyStatus.Available or PropertyStatus.OnHold
        && p.LastConfirmedAt < utcNow.AddDays(-(p.Purpose == PropertyPurpose.Rent ? s.RentFreshDays : s.SaleFreshDays));

    /// <summary>A partner project not confirmed with its builder within the dealer's sale freshness days (D21); an own one never.</summary>
    public static bool IsStale(ProjectDocument project, EstateSettingsDocument s, DateTime utcNow) =>
        project.Kind == Models.Projects.ProjectKind.Partner && project.LastConfirmedAt < utcNow.AddDays(-s.SaleFreshDays);
}

/// <summary>
/// Free text a dealer typed, made safe for the public — the website, share pages, the messages a customer gets. A
/// customer must reach the property through the dealer, so contact details always go: mobile and phone numbers, email
/// addresses. For a resale listing (<see cref="Listing(string?)"/>) the exact unit goes too: "H.No 1234", "House No.
/// 1234", "#1234", "Flat C-1204", "Plot No 45", "Shop 12", "SCO 123", "मकान नंबर 1234". The place stays: "Corner, Sector
/// 22, Phase 11, Chandigarh"; and so do sizes and prices ("5 marla", "Flat 3 BHK", "₹1,60,00,000"). A builder's project
/// (<see cref="Contact(string?)"/>) keeps its address and plot numbers — its site is public — and loses only contacts.
/// </summary>
public static partial class PublicText
{
    /// <summary>The text without phone numbers or email addresses; null when nothing is left.</summary>
    public static string? Contact(string? value) => Tidy(Scrub(value, unit: false));

    /// <summary>The text without contacts or the house, flat, plot or shop number; null when nothing is left.</summary>
    public static string? Listing(string? value) => Tidy(Scrub(value, unit: true));

    /// <summary>A required text (a title, a name): scrubbed, or the original when scrubbing would empty it.</summary>
    public static string ListingOr(string value) => Listing(value) ?? value;

    public static List<string> Listing(IEnumerable<string> values) => [.. values.Select(Listing).OfType<string>()];

    public static List<string> Contact(IEnumerable<string> values) => [.. values.Select(Contact).OfType<string>()];

    /// <summary>A video or drone link: only a web address, so no number hides in it.</summary>
    public static string? Link(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri.ToString() : null;

    private static string? Scrub(string? value, bool unit)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = Email().Replace(value, " ");
        text = Mobile().Replace(text, " ");
        text = Landline().Replace(text, " ");
        if (unit)
        {
            text = UnitNumber().Replace(text, " ");
            text = HashNumber().Replace(text, " ");
        }
        return text;
    }

    private static string? Tidy(string? value)
    {
        if (value is null)
            return null;
        var lines = value.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var l = Gaps().Replace(line, " ");
            l = EmptyCommas().Replace(l, ",");
            l = SpaceBeforeComma().Replace(l, ",");
            l = CommaBeforeStop().Replace(l, "$1");
            return l.Trim().Trim(',', ';', ':', '-', '|', '/').Trim();
        });
        var text = string.Join("\n", lines).Trim();
        return text.Length == 0 || !text.Any(char.IsLetterOrDigit) ? null : text;
    }

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    // An Indian mobile: optional +91 / 91 / 0, then 10 digits from 6–9, spaces, dots or dashes between them — never
    // commas, so "₹1,60,00,000" is a price, not a phone.
    [GeneratedRegex(@"(?<![\p{N}\p{L}])(?:\+\s?91[\s.-]?|91[\s.-]|0)?[6-9](?:[\s.-]?\p{N}){9}(?![\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex Mobile();

    // A landline with its STD code: 0172-2701234, 0172 270 1234, +91 172 2701234.
    [GeneratedRegex(@"(?<![\p{N}\p{L}])(?:\+\s?91[\s.-]?|0)\p{N}{2,4}[\s.-]?\p{N}{3,4}[\s.-]?\p{N}{3,4}(?![\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex Landline();

    // "H.No 1234", "House No. 12-B", "Flat C-1204", "Plot No: 45", "Shop 12", "SCO 123", "Kothi No 7", "मकान नंबर 1234" — the
    // word and its number. Not "Flat 3 BHK", "Plot 200 gaj", "House 5 marla", "Flat 2nd floor": a size, a count or a floor.
    [GeneratedRegex(
        @"(?<![\p{L}])(?:h\s*\.?\s*no|house(?:\s*(?:no|number|num))?|makaa?n(?:\s*(?:no|number))?|kothi(?:\s*(?:no|number))?|flat(?:\s*(?:no|number))?|plot(?:\s*(?:no|number))?|shop(?:\s*(?:no|number))?|unit(?:\s*(?:no|number))?|apartment(?:\s*(?:no|number))?|apt|villa(?:\s*(?:no|number))?|booth(?:\s*(?:no|number))?|showroom(?:\s*(?:no|number))?|office(?:\s*(?:no|number))?|sc[of](?:\s*(?:no|number))?|dss(?:\s*(?:no|number))?|मकान|फ्लैट|फ़्लैट|प्लॉट|प्लाट|कोठी|दुकान)(?:\s*(?:नंबर|नं|न)\.?)?\s*\.?\s*[:#-]?\s*(?:[\p{L}]{1,2}\s?-\s?)?\p{N}{1,5}(?![\p{N}])(?:[\p{L}](?![\p{L}]))?(?:\s?[-/]\s?[\p{L}\p{N}]{1,5}(?![\p{L}\p{N}]))?(?!\s*(?:bhk|rk|bed|bedroom|room|floor|storey|story|marla|marle|kanal|gaj|yard|yd|sq|ft|feet|foot|acre|bigha|cr|crore|lakh|lac|l\b|k\b|%|st\b|nd\b|rd\b|th\b|मरला|कनाल|गज))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitNumber();

    // Chandigarh's "#1234" / "# 1234-B": a house number by its sign.
    [GeneratedRegex(@"#\s?\p{N}{1,5}(?:\s?-?\s?[\p{L}\p{N}]{1,3}(?![\p{L}\p{N}]))?", RegexOptions.CultureInvariant)]
    private static partial Regex HashNumber();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Gaps();

    [GeneratedRegex(@"(?:\s*,){2,}")]
    private static partial Regex EmptyCommas();

    [GeneratedRegex(@"\s+,")]
    private static partial Regex SpaceBeforeComma();

    // "Sharma ji, . 5 marla" → "Sharma ji. 5 marla": what was between them is gone.
    [GeneratedRegex(@"\s*,?\s+([.;!?])")]
    private static partial Regex CommaBeforeStop();
}
