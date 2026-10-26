using System.Text.RegularExpressions;
using FluentValidation;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNova_Packages.Security;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Common;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Parties;

namespace ZeroNovaRealEstate.Application.Features.Parties;

/// <summary>Adds (no id) or changes a person; Aadhaar and PAN are sealed (D11), empty keeps the stored ones.</summary>
public sealed record SavePartyCommand(string? PartyId, PartyInput Input) : IRequest<PartyDto>;

public sealed partial class SavePartyValidator : AbstractValidator<SavePartyCommand>
{
    public SavePartyValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Input.RelativeName).MaximumLength(120);
        RuleFor(x => x.Input.Address).MaximumLength(500);
        RuleFor(x => x.Input.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Input.Email));
        RuleFor(x => x.Input.Aadhaar).Must(a => a is null || a.Trim().Length == 0 || Aadhaar().IsMatch(a.Replace(" ", "")))
            .WithMessage("An Aadhaar number has 12 digits.");
        RuleFor(x => x.Input.Pan).Must(p => p is null || p.Trim().Length == 0 || Pan().IsMatch(p.Trim().ToUpperInvariant()))
            .WithMessage("A PAN looks like ABCDE1234F.");
    }

    [GeneratedRegex(@"^\d{12}$")]
    private static partial Regex Aadhaar();

    [GeneratedRegex(@"^[A-Z]{5}\d{4}[A-Z]$")]
    private static partial Regex Pan();
}

public sealed class SavePartyHandler(
    IPartyRepository parties, IPropertyRepository properties, ISecretProtector secrets, EstateScope scope, IRequestContext context, PartyContacts contacts)
    : IRequestHandler<SavePartyCommand, PartyDto>
{
    public async Task<PartyDto> Handle(SavePartyCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var party = string.IsNullOrEmpty(request.PartyId)
            ? new PartyDocument { Id = parties.GetNewId, Name = string.Empty }
            : await PartyLookup.RequireAsync(parties, request.PartyId!, ct);
        var company = context.CompanyId ?? throw new ValidatorException("No active company. Select a company first.");

        party.Name = EstateText.Clean(input.Name)!;
        party.Relation = input.Relation;
        party.RelativeName = EstateText.Clean(input.RelativeName);
        party.Phone = EstateText.Phone(input.Phone);
        party.WhatsApp = EstateText.Phone(input.WhatsApp);
        party.Email = EstateText.Clean(input.Email);
        party.Address = EstateText.Clean(input.Address);
        party.LeadId = EstateText.Clean(input.LeadId);

        if (input.Aadhaar?.Replace(" ", "") is { Length: 12 } aadhaar)
        {
            party.AadhaarSealed = Seal(company, aadhaar);
            party.AadhaarLast4 = aadhaar[^4..];
        }
        if (input.Pan?.Trim().ToUpperInvariant() is { Length: 10 } pan)
        {
            party.PanSealed = Seal(company, pan);
            party.PanLast4 = pan[^4..];
        }

        if (string.IsNullOrEmpty(request.PartyId))
            await parties.AddAsync(party, ct);
        else
            await parties.UpdateAsync(party, ct);
        await contacts.SyncAsync(party, ct);
        return await PartyMapping.ToDtoAsync(party, properties, scope, ct);
    }

    private string Seal(string company, string value) => secrets.IsConfigured
        ? secrets.Protect(company, value)
        : throw new ValidatorException("Aadhaar and PAN cannot be saved yet: encryption is not set up. Save the person without them for now.");
}

/// <summary>People in the dealer's papers, newest first, by name or phone.</summary>
public sealed record GetPartiesQuery(string? Search, int Take = 50) : IRequest<IReadOnlyList<PartyDto>>;

public sealed class GetPartiesHandler(IPartyRepository parties, IPropertyRepository properties, EstateScope scope) : IRequestHandler<GetPartiesQuery, IReadOnlyList<PartyDto>>
{
    public async Task<IReadOnlyList<PartyDto>> Handle(GetPartiesQuery request, CancellationToken ct)
    {
        var query = parties.GetDocuments();
        if (EstateText.Clean(request.Search) is { } search)
        {
            var digits = new string(search.Where(char.IsDigit).ToArray());
            var lower = search.ToLowerInvariant();
            query = digits.Length >= 4
                ? query.Where(p => p.Phone != null && p.Phone.Contains(digits))
                : query.Where(p => p.Name.ToLower().Contains(lower));
        }
        var rows = await query.OrderByDescending(p => p.Id).Take(Math.Clamp(request.Take, 1, 200)).ToListAsync(ct);
        var dtos = new List<PartyDto>(rows.Count);
        foreach (var row in rows)
            dtos.Add(await PartyMapping.ToDtoAsync(row, properties, scope, ct));
        return dtos;
    }
}

public sealed record GetPartyQuery(string PartyId) : IRequest<PartyDto>;

public sealed class GetPartyHandler(IPartyRepository parties, IPropertyRepository properties, EstateScope scope) : IRequestHandler<GetPartyQuery, PartyDto>
{
    public async Task<PartyDto> Handle(GetPartyQuery request, CancellationToken ct) =>
        await PartyMapping.ToDtoAsync(await PartyLookup.RequireAsync(parties, request.PartyId, ct), properties, scope, ct);
}

internal static class PartyLookup
{
    public static async Task<PartyDocument> RequireAsync(IPartyRepository parties, string id, CancellationToken ct) =>
        await parties.GetDocument(id).FirstOrDefaultAsync(ct) ?? throw new ValidatorException("Person not found.");
}

public static class PartyMapping
{
    /// <summary>Aadhaar and PAN only as their last four; phones only with POWV; the properties this person owns.</summary>
    public static async Task<PartyDto> ToDtoAsync(PartyDocument p, IPropertyRepository properties, EstateScope scope, CancellationToken ct)
    {
        var owned = await properties.GetDocuments(x => x.Owners.Any(o => o.PartyId == p.Id)).Select(x => x.Id).ToListAsync(ct);
        var sees = scope.SeesOwners;
        return new PartyDto(p.Id, p.Name, p.Relation, p.RelativeName, sees ? p.Phone : null, sees ? p.WhatsApp : null, sees ? p.Email : null,
            sees ? p.Address : null, p.AadhaarLast4, p.PanLast4, p.LeadId, owned);
    }
}
