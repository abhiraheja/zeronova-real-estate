using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ZeroNova_Packages.Security;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;

namespace ZeroNovaRealEstate.Application.Features.Agreements;

/// <summary>The values an agreement is filled with: single values by key, and lists (payments, schedule, witnesses…) of item values.</summary>
public sealed record AgreementValues(IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> Lists);

/// <summary>
/// Fills agreements (D12): the deal's people, property and money as values; the ready templates rendered to text; the
/// text to a legal-size Word file; and the dealer's own Word templates filled in place. Amounts in figures and in Indian
/// words, he/she/they from the parties, one execution date everywhere.
/// </summary>
public static partial class AgreementEngine
{
    public const string Blank = "__________";

    // ── Values ───────────────────────────────────────────────────────────────────────────────────────

    public static AgreementValues Values(DealDocument d, PropertyDocument p, IReadOnlyDictionary<string, PartyDocument> people, EstateSettingsDocument s,
        ISecretProtector secrets, string companyId)
    {
        var sellers = d.SellerIds.Select(id => people.GetValueOrDefault(id)).OfType<PartyDocument>().ToList();
        var buyers = d.BuyerIds.Select(id => people.GetValueOrDefault(id)).OfType<PartyDocument>().ToList();
        var paid = d.Payments.Sum(x => x.Amount);
        var balance = d.Price - paid;
        var v = new Dictionary<string, string>
        {
            ["execution.place"] = d.ExecutionPlace ?? Blank,
            ["execution.date"] = IndianWords.Date(d.ExecutionDate),
            ["sellers.text"] = PartiesText(sellers, secrets, companyId),
            ["buyers.text"] = PartiesText(buyers, secrets, companyId),
            ["sellers.names"] = Names(sellers),
            ["buyers.names"] = Names(buyers),
            ["property.description"] = d.PropertyDescription ?? Describe(p),
            ["property.code"] = p.Code,
            ["price.figures"] = IndianWords.Figures(d.Price),
            ["price.words"] = IndianWords.Rupees(d.Price),
            ["paid.figures"] = paid > 0 ? IndianWords.Figures(paid) : string.Empty,
            ["paid.words"] = IndianWords.Rupees(paid),
            ["balance.figures"] = IndianWords.Figures(balance),
            ["balance.words"] = IndianWords.Rupees(balance),
            ["final.date"] = IndianWords.Date(d.FinalDate ?? d.Schedule.Select(x => x.DueDate).Where(x => x is not null).Max()),
            ["title.note"] = d.TitleNote ?? string.Empty,
            ["inclusions.any"] = d.Inclusions.Count > 0 ? "yes" : string.Empty,
            ["dealer.name"] = s.DealerName ?? string.Empty,
            ["dealer.rera"] = s.ReraNumber ?? string.Empty,
        };
        if (d.EarnestPercent is { } pct)
        {
            v["earnest.percent"] = pct.ToString("0.##");
            v["earnest.figures"] = IndianWords.Figures(Math.Round(d.Price * pct / 100m, 0));
            v["earnest.words"] = IndianWords.Rupees(Math.Round(d.Price * pct / 100m, 0));
        }
        if (d.Kind == PropertyPurpose.Sale && d.Price >= 50_00_000m)
        {
            v["tds.figures"] = IndianWords.Figures(Math.Round(d.Price / 100m, 0));
            v["tds.words"] = IndianWords.Rupees(Math.Round(d.Price / 100m, 0));
        }
        foreach (var (side, list) in new[] { ("seller", sellers), ("buyer", buyers) })
        {
            var (pronoun, possessive, has) = Pronouns(list);
            v[$"{side}.pronoun"] = pronoun;
            v[$"{side}.possessive"] = possessive;
            v[$"{side}.has"] = has;
        }

        var lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
        {
            ["payments"] = [.. d.Payments.Select((x, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                [".n"] = (i + 1).ToString(), [".mode"] = ModeLabel(x.Mode), [".figures"] = IndianWords.Figures(x.Amount), [".words"] = IndianWords.Rupees(x.Amount),
                [".reference"] = x.Reference ?? string.Empty, [".refLabel"] = x.Mode is PaymentMode.Cheque or PaymentMode.DemandDraft ? "No." : "UTR No",
                [".bank"] = x.Bank ?? string.Empty, [".date"] = IndianWords.ShortDate(x.Date),
                [".payer"] = x.PayerPartyId is { } payer ? people.GetValueOrDefault(payer)?.Name ?? string.Empty : string.Empty,
            })],
            ["schedule"] = [.. d.Schedule.Select((x, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                [".n"] = (i + 1).ToString(), [".mode"] = x.Mode is { } m ? ModeLabel(m) : "Payment", [".figures"] = IndianWords.Figures(x.Amount),
                [".words"] = IndianWords.Rupees(x.Amount), [".date"] = IndianWords.ShortDate(x.DueDate),
            })],
            ["witnesses"] = [.. d.Witnesses.Select((x, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                [".n"] = (i + 1).ToString(), [".name"] = x.Name, [".address"] = x.Address ?? string.Empty,
            })],
            ["inclusions"] = [.. d.Inclusions.Select((x, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [".n"] = (i + 1).ToString(), [".text"] = x })],
            ["terms"] = [.. d.Terms.Select((x, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [".n"] = (i + 1).ToString(), [".text"] = x })],
        };
        return new AgreementValues(v, lists);
    }

    private static string PartiesText(IReadOnlyList<PartyDocument> people, ISecretProtector secrets, string companyId)
    {
        if (people.Count == 0)
            return Blank;
        return string.Join(" and ", people.Select(x =>
        {
            var parts = new List<string> { $"{Title(x)}{x.Name}".Trim() };
            if (DealChecksRelation(x) is { } rel)
                parts.Add($"{rel} {x.RelativeName ?? Blank}");
            if (Open(x.AadhaarSealed, secrets, companyId) is { } aadhaar)
                parts.Add($"having Aadhaar No. {aadhaar[..4]} {aadhaar[4..8]} {aadhaar[8..]}");
            if (Open(x.PanSealed, secrets, companyId) is { } pan)
                parts.Add($"PAN {pan}");
            parts.Add($"R/O {x.Address ?? Blank}");
            return string.Join(' ', parts);
        }));
    }

    private static string? Open(string? sealedValue, ISecretProtector secrets, string companyId)
    {
        if (sealedValue is null || !secrets.IsConfigured)
            return null;
        try
        {
            return secrets.Unprotect(companyId, sealedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static string? DealChecksRelation(PartyDocument x) => Deals.DealChecks.RelationLabel(x.Relation);

    private static string Title(PartyDocument x) => x.Relation switch
    {
        RelationKind.SonOf => "Mr. ",
        RelationKind.WifeOf => "Mrs. ",
        RelationKind.DaughterOf => "Ms. ",
        _ => string.Empty,
    };

    private static string Names(IReadOnlyList<PartyDocument> people) =>
        people.Count == 0 ? Blank : string.Join(" and ", people.Select(x => $"{Title(x)}{x.Name}".Trim()));

    /// <summary>he/his/has, she/her/has, or they/their/have for several (or when it is not known).</summary>
    private static (string Pronoun, string Possessive, string Has) Pronouns(IReadOnlyList<PartyDocument> people) => people switch
    {
        [{ Relation: RelationKind.SonOf }] => ("he", "his", "has"),
        [{ Relation: RelationKind.WifeOf or RelationKind.DaughterOf }] => ("she", "her", "has"),
        _ => ("they", "their", "have"),
    };

    private static string ModeLabel(PaymentMode mode) => mode switch
    {
        PaymentMode.Neft => "NEFT",
        PaymentMode.Rtgs => "RTGS",
        PaymentMode.Imps => "IMPS",
        PaymentMode.Upi => "UPI",
        PaymentMode.DemandDraft => "Demand Draft",
        _ => mode.ToString(),
    };

    /// <summary>"the 3 BHK Flat No. 918, 9th Floor, Tower 5, Beverly Golf Avenue, Sector 65, Mohali, comprising a total area of 2,072.05 sq ft".</summary>
    public static string Describe(PropertyDocument p)
    {
        var parts = new List<string>();
        if (p.UnitNumber is { } unit)
            parts.Add($"No. {unit}");
        if (p.Floor is { } floor)
            parts.Add(floor == 0 ? "Ground Floor" : floor < 0 ? "Basement" : $"{Ordinal(floor)} Floor");
        foreach (var x in new[] { p.Block, p.Society, p.Locality, p.City })
            if (!string.IsNullOrWhiteSpace(x))
                parts.Add(x);
        var area = p.SuperArea ?? p.BuiltUpArea ?? p.PlotArea;
        var text = $"the {p.Title.Split(" in ")[0]} {string.Join(", ", parts)}";
        if (area is not null)
            text += $", comprising a total area of {AreaMath.Label(area)}";
        if (p.CarpetArea is { } carpet)
            text += $" (carpet area {AreaMath.Label(carpet)})";
        return text;
    }

    private static string Ordinal(int n) =>
        n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    // ── Ready templates to text ──────────────────────────────────────────────────────────────────────

    public static string Render(string body, AgreementValues values)
    {
        var lines = new List<string>();
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var loop = Loop().Match(raw);
            if (loop.Success)
            {
                foreach (var item in values.Lists.GetValueOrDefault(loop.Groups[1].Value) ?? [])
                {
                    var merged = new Dictionary<string, string>(values.Values);
                    foreach (var (k, val) in item)
                        merged[k] = val;
                    lines.Add(Inline(loop.Groups[2].Value, merged));
                }
                continue;
            }
            var line = Inline(raw, values.Values);
            if (line.Trim().Length == 0 && raw.Trim().Length > 0)
                continue;   // a line that was only a condition not met
            lines.Add(line);
        }
        return string.Join('\n', lines).Trim('\n');
    }

    private static string Inline(string text, IReadOnlyDictionary<string, string> values)
    {
        var kept = Conditional().Replace(text, m => string.IsNullOrWhiteSpace(values.GetValueOrDefault(m.Groups[1].Value)) ? string.Empty : m.Groups[2].Value);
        return Placeholder().Replace(kept, m => values.GetValueOrDefault(m.Groups[1].Value) is { Length: > 0 } v ? v : Blank);
    }

    /// <summary>The placeholders a text (or a Word file's text) asks for.</summary>
    public static IReadOnlyList<string> Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Concat(Loop().Matches(text).Select(m => "#" + m.Groups[1].Value)).Distinct()];

    [GeneratedRegex(@"\{\{#([\w.]+)\}\}(.*?)\{\{/\1\}\}")]
    private static partial Regex Loop();

    [GeneratedRegex(@"\{\{\?([\w.]+)\}\}(.*?)\{\{/\1\}\}")]
    private static partial Regex Conditional();

    [GeneratedRegex(@"\{\{([\w.]+)\}\}")]
    private static partial Regex Placeholder();

    // ── Text to Word ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The text as a Word file on legal paper (for stamp paper): <c># </c> lines a centred bold heading, every other line a
    /// justified paragraph in Times New Roman 12. Always editable in Word.
    /// </summary>
    public static byte[] ToDocx(string text)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var heading = line.StartsWith("# ", StringComparison.Ordinal);
                var content = heading ? line[2..] : line;
                var look = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = heading ? "28" : "24" });
                if (heading)
                    look.Append(new Bold());
                var run = new Run(look, new Text(content) { Space = SpaceProcessingModeValues.Preserve });
                body.Append(new Paragraph(new ParagraphProperties(
                    new Justification { Val = heading ? JustificationValues.Center : JustificationValues.Both },
                    new SpacingBetweenLines { After = "120", Line = "300", LineRule = LineSpacingRuleValues.Auto }), run));
            }
            body.Append(new SectionProperties(
                new PageSize { Width = 12240, Height = 20160 },
                new PageMargin { Top = 2880, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720, Gutter = 0 }));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return stream.ToArray();
    }

    // ── The dealer's own Word file ───────────────────────────────────────────────────────────────────

    /// <summary>The text of a Word file, paragraph by paragraph (to find its placeholders, or preview it filled).</summary>
    public static string TextOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        var sb = new StringBuilder();
        foreach (var p in Paragraphs(doc))
            sb.AppendLine(string.Concat(p.Descendants<Text>().Select(t => t.Text)));
        return sb.ToString();
    }

    /// <summary>
    /// Fills a Word template in place: each paragraph with a placeholder is joined into its first run (keeping that run's
    /// look) and filled; a paragraph with <c>{{#list}}…{{/list}}</c> is repeated for each item.
    /// </summary>
    public static byte[] Fill(byte[] template, AgreementValues values)
    {
        using var stream = new MemoryStream();
        stream.Write(template);
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            foreach (var paragraph in Paragraphs(doc).ToList())
            {
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
                if (!text.Contains("{{", StringComparison.Ordinal))
                    continue;
                var loop = Loop().Match(text);
                if (loop.Success)
                {
                    var items = values.Lists.GetValueOrDefault(loop.Groups[1].Value) ?? [];
                    foreach (var item in items)
                    {
                        var merged = new Dictionary<string, string>(values.Values);
                        foreach (var (k, v) in item)
                            merged[k] = v;
                        var copy = (Paragraph)paragraph.CloneNode(true);
                        SetText(copy, text.Replace(loop.Value, Inline(loop.Groups[2].Value, merged)));
                        paragraph.InsertBeforeSelf(copy);
                    }
                    paragraph.Remove();
                    continue;
                }
                SetText(paragraph, Inline(text, values.Values));
            }
            doc.MainDocumentPart?.Document.Save();
        }
        return stream.ToArray();
    }

    private static IEnumerable<Paragraph> Paragraphs(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart;
        if (main is null)
            yield break;
        foreach (var p in main.Document.Descendants<Paragraph>())
            yield return p;
        foreach (var header in main.HeaderParts)
            foreach (var p in header.Header.Descendants<Paragraph>())
                yield return p;
        foreach (var footer in main.FooterParts)
            foreach (var p in footer.Footer.Descendants<Paragraph>())
                yield return p;
    }

    private static void SetText(Paragraph paragraph, string text)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0)
            return;
        texts[0].Text = text;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var t in texts.Skip(1))
            t.Text = string.Empty;
    }
}
