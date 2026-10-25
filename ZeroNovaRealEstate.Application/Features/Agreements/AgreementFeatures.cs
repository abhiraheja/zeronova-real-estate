using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNova_Packages.Security;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Deals;

namespace ZeroNovaRealEstate.Application.Features.Agreements;

/// <summary>The ready templates and the dealer's own Word templates.</summary>
public sealed record GetAgreementTemplatesQuery : IRequest<IReadOnlyList<AgreementTemplateDto>>;

public sealed class GetAgreementTemplatesHandler(IAgreementTemplateRepository templates) : IRequestHandler<GetAgreementTemplatesQuery, IReadOnlyList<AgreementTemplateDto>>
{
    public async Task<IReadOnlyList<AgreementTemplateDto>> Handle(GetAgreementTemplatesQuery request, CancellationToken ct)
    {
        var own = await templates.GetDocuments().OrderBy(t => t.Name).ToListAsync(ct);
        return [.. BuiltInTemplates.All.Select(t => new AgreementTemplateDto(t.Id, t.Name, t.Kind, true, AgreementEngine.Placeholders(t.Body))),
            .. own.Select(t => new AgreementTemplateDto(t.Id, t.Name, t.Kind, false, t.Placeholders))];
    }
}

/// <summary>
/// Adds (no id) or replaces the dealer's own Word template (D12): a .docx with <c>{{placeholders}}</c> — the same names
/// the ready templates use (<c>{{sellers.text}}</c>, <c>{{price.words}}</c>, a line with <c>{{#payments}}…{{/payments}}</c>).
/// </summary>
public sealed record SaveAgreementTemplateCommand(string? TemplateId, AgreementTemplateInput Input) : IRequest<AgreementTemplateDto>;

public sealed class SaveAgreementTemplateValidator : AbstractValidator<SaveAgreementTemplateCommand>
{
    public SaveAgreementTemplateValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Input.DocxBase64).NotEmpty().MaximumLength(3_000_000).WithMessage("The Word file must be under 2 MB.");
    }
}

public sealed class SaveAgreementTemplateHandler(IAgreementTemplateRepository templates) : IRequestHandler<SaveAgreementTemplateCommand, AgreementTemplateDto>
{
    public async Task<AgreementTemplateDto> Handle(SaveAgreementTemplateCommand request, CancellationToken ct)
    {
        byte[] bytes;
        string text;
        try
        {
            bytes = Convert.FromBase64String(request.Input.DocxBase64);
            text = AgreementEngine.TextOf(bytes);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            throw new ValidatorException("This is not a Word (.docx) file.");
        }

        var row = string.IsNullOrEmpty(request.TemplateId)
            ? new AgreementTemplateDocument { Id = templates.GetNewId, Name = string.Empty, Docx = [] }
            : await templates.GetDocument(request.TemplateId!).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Template not found.");
        row.Name = EstateText.Clean(request.Input.Name)!;
        row.Kind = request.Input.Kind;
        row.Docx = bytes;
        row.Placeholders = [.. AgreementEngine.Placeholders(text)];
        if (string.IsNullOrEmpty(request.TemplateId))
            await templates.AddAsync(row, ct);
        else
            await templates.UpdateAsync(row, ct);
        return new AgreementTemplateDto(row.Id, row.Name, row.Kind, false, row.Placeholders);
    }
}

public sealed record DeleteAgreementTemplateCommand(string TemplateId) : IRequest<bool>;

public sealed class DeleteAgreementTemplateHandler(IAgreementTemplateRepository templates) : IRequestHandler<DeleteAgreementTemplateCommand, bool>
{
    public async Task<bool> Handle(DeleteAgreementTemplateCommand request, CancellationToken ct)
    {
        await templates.DeleteAsync(request.TemplateId, ct);
        return true;
    }
}

/// <summary>
/// The agreement filled from the deal, as text to read and change before it becomes Word (a ready template), or as the
/// filled text of the dealer's own file (read-only here; it is edited in Word). With the deal's warnings.
/// </summary>
public sealed record PreviewAgreementQuery(string DealId, string TemplateId) : IRequest<AgreementPreviewDto>;

public sealed class PreviewAgreementHandler(AgreementSource source) : IRequestHandler<PreviewAgreementQuery, AgreementPreviewDto>
{
    public async Task<AgreementPreviewDto> Handle(PreviewAgreementQuery request, CancellationToken ct)
    {
        var (deal, values, warnings, builtIn, own) = await source.LoadAsync(request.DealId, request.TemplateId, ct);
        return builtIn is not null
            ? new AgreementPreviewDto(builtIn.Id, builtIn.Name, AgreementEngine.Render(builtIn.Body, values), true, warnings)
            : new AgreementPreviewDto(own!.Id, own.Name, AgreementEngine.TextOf(AgreementEngine.Fill(own.Docx, values)), false, warnings);
    }
}

/// <summary>
/// The agreement as a Word file (legal size), always editable (D12). For a ready template the dealer's edited text is
/// used when given; the dealer's own file is filled in place.
/// </summary>
public sealed record BuildAgreementCommand(string DealId, string TemplateId, string? EditedText) : IRequest<AgreementFileDto>;

public sealed class BuildAgreementHandler(AgreementSource source) : IRequestHandler<BuildAgreementCommand, AgreementFileDto>
{
    public async Task<AgreementFileDto> Handle(BuildAgreementCommand request, CancellationToken ct)
    {
        var (deal, values, warnings, builtIn, own) = await source.LoadAsync(request.DealId, request.TemplateId, ct);
        var bytes = builtIn is not null
            ? AgreementEngine.ToDocx(string.IsNullOrWhiteSpace(request.EditedText) ? AgreementEngine.Render(builtIn.Body, values) : request.EditedText)
            : AgreementEngine.Fill(own!.Docx, values);
        var name = (builtIn?.Name ?? own!.Name).Split('—')[0].Trim().Replace(' ', '_');
        return new AgreementFileDto($"{deal.Code}_{name}.docx", Convert.ToBase64String(bytes), warnings);
    }
}

/// <summary>A deal, its values and warnings, and the template asked for — what preview and build share.</summary>
public sealed class AgreementSource(IDealRepository deals, IPropertyRepository properties, IPartyRepository parties, IEstateSettingsRepository settings,
    IAgreementTemplateRepository templates, ISecretProtector secrets, EstateScope scope, IRequestContext context)
{
    public async Task<(DealDocument Deal, AgreementValues Values, IReadOnlyList<string> Warnings, BuiltInTemplates.Template? BuiltIn, AgreementTemplateDocument? Own)>
        LoadAsync(string dealId, string templateId, CancellationToken ct)
    {
        var deal = await deals.GetDocument(dealId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Deal not found.");
        if (!scope.SeesAllDeals && deal.AgentId != scope.UserId)
            throw new ValidatorException("This deal is another agent's.");
        var property = await properties.GetDocument(deal.PropertyId, true).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Property not found.");
        var ids = deal.SellerIds.Concat(deal.BuyerIds).Concat(deal.Payments.Select(p => p.PayerPartyId).OfType<string>())
            .Concat(property.Owners.Select(o => o.PartyId)).Distinct().ToList();
        var people = (await parties.GetDocuments(p => ids.Contains(p.Id), true).ToListAsync(ct)).ToDictionary(p => p.Id);
        var s = await EstateSettings.LoadAsync(settings, ct);

        var builtIn = BuiltInTemplates.Find(templateId);
        var own = builtIn is null
            ? await templates.GetDocument(templateId).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Template not found.")
            : null;
        var company = context.CompanyId ?? throw new ValidatorException("No active company. Select a company first.");
        var warnings = DealChecks.Warnings(deal, property, people).ToList();
        if (!secrets.IsConfigured && people.Values.Any(p => p.AadhaarSealed is not null))
            warnings.Add("Aadhaar numbers cannot be opened yet (encryption not set up) — fill them by hand in Word.");
        return (deal, AgreementEngine.Values(deal, property, people, s, secrets, company), warnings, builtIn, own);
    }
}
