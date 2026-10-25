using ZeroNovaRealEstate.Models.Deals;

namespace ZeroNovaRealEstate.Application.Features.Agreements;

/// <summary>
/// The ready templates (D12): the owner's two Agreement to Sell formats, an earnest-money receipt and an 11-month rent
/// agreement, in a small line language — <c># </c> a centred bold heading; <c>{{key}}</c> a value; a line with
/// <c>{{#list}} … {{/list}}</c> repeated for each item (<c>{{.field}}</c> inside); <c>{{?key}} … {{/key}}</c> kept only
/// when the value is there. A value not known prints as a blank line to fill by hand.
/// </summary>
public static class BuiltInTemplates
{
    public sealed record Template(string Id, string Name, AgreementKind Kind, string Body);

    public static readonly IReadOnlyList<Template> All =
    [
        new("builtin:format-a", "Agreement to Sell — builder flat (Format A)", AgreementKind.AgreementToSell, FormatA),
        new("builtin:format-b", "Agreement to Sell — with payment schedule (Format B)", AgreementKind.AgreementToSell, FormatB),
        new("builtin:token-receipt", "Earnest money (biana) receipt", AgreementKind.TokenReceipt, TokenReceipt),
        new("builtin:rent-11", "Rent agreement — 11 months", AgreementKind.RentAgreement, Rent),
    ];

    public static Template? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private const string FormatA = """
# AGREEMENT TO SELL
This AGREEMENT TO SELL is executed at {{execution.place}} on this {{execution.date}} by and between
{{sellers.text}} hereinafter referred to as "THE FIRST PARTY".
AND
{{buyers.text}} hereinafter referred to as "THE SECOND PARTY".
The expression of the terms the 'FIRST PARTY' and the 'SECOND PARTY' wherever they occur in the body of this Agreement to Sell, shall mean and include {{seller.possessive}} legal heirs, successors, legal representatives, administrators, executors, transferee(s), beneficiary(ies), legatee(s), nominees and assignee(s).
WHEREAS the FIRST PARTY for {{seller.possessive}} bonafide needs and requirements has agreed to sell, convey, transfer and assign to the SECOND PARTY and the SECOND PARTY has agreed to purchase the undivided, indivisible ownership rights in the said freehold built up property with all rights, title and interest, easements, privileges and appurtenances thereto.
WHEREAS this Agreement to Sell (hereinafter referred to as the AGREEMENT) pertains to {{property.description}} (hereinafter referred to as the SAID PROPERTY).
{{?title.note}}WHEREAS the SAID PROPERTY is owned and in full physical possession of the FIRST PARTY as per {{title.note}}.{{/title.note}}
{{?inclusions.any}}WHEREAS, the FIRST PARTY has secured the following:{{/inclusions.any}}
{{#inclusions}}{{.n}}. {{.text}}{{/inclusions}}
WHEREAS the FIRST PARTY has further represented that {{seller.pronoun}} {{seller.has}} absolute, unfettered rights and absolute authority to sell, dispose off and transfer the SAID PROPERTY and
That the FIRST PARTY and the SECOND PARTY have agreed to a net sale consideration of {{price.figures}} ({{price.words}}) for the SAID PROPERTY, against which earnest money has been paid by the SECOND PARTY to the FIRST PARTY as under:
{{#payments}}{{.mode}} of {{.figures}} ({{.words}}){{?.reference}} having {{.refLabel}} {{.reference}}{{/.reference}}{{?.bank}} Bank: {{.bank}}{{/.bank}} Dt. {{.date}}{{?.payer}} from {{.payer}}{{/.payer}}{{/payments}}
The remaining balance sum of {{balance.figures}} ({{balance.words}}) will be paid by the SECOND PARTY to the FIRST PARTY before or by {{final.date}}.
NOW THIS AGREEMENT TO SELL WITNESSETH AS UNDER:-
1. That the FIRST PARTY hereby agrees to grant, convey, sell, transfer and assign all {{seller.possessive}} rights, titles and interests in the SAID PROPERTY described above, to the SECOND PARTY, on the terms and conditions herein contained.
2. That the balance and final amount of {{balance.figures}} ({{balance.words}}) will be handed over to the FIRST PARTY upon receipt of the no dues certificate from the builder and upon the transfer of ownership in the name of the SECOND PARTY in the books of the builder along with a fresh allotment letter in the name of the SECOND PARTY, and the FIRST PARTY will give full, vacant and peaceful possession of the SAID PROPERTY to the SECOND PARTY.
3. That the FIRST PARTY hereby assures the SECOND PARTY that the FIRST PARTY has neither done nor has been party to any act whereby {{seller.possessive}} rights and title of the said property may in any way be impaired or whereby the FIRST PARTY may be prevented from transferring the said property to the SECOND PARTY.
4. That the FIRST PARTY assures the SECOND PARTY that the said property is free from all kinds of encumbrances such as prior sale, gift, mortgage, will, trust, exchange, lease, legal flaw, claims, loan, surety, security, lien, court injunction, litigation, stay order, notices, charges, family or religious dispute, acquisition, attachment in the decree of any court, hypothecation, income tax or wealth tax attachment, or any other registered or unregistered encumbrance whatsoever prior to this Agreement to Sell, and if it is ever proved otherwise, the FIRST PARTY will be liable to make good the loss suffered by the SECOND PARTY and keep the SECOND PARTY indemnified against all such losses.
5. That the SECOND PARTY, after receiving the transfer of allotment from the builder, will be well within {{buyer.possessive}} rights to get this allotment registered as a sale deed with the revenue authorities and the FIRST PARTY shall have no objection to it whatsoever.
6. That none of the parties shall back out from this agreement at any stage in future. If the FIRST PARTY backs out, {{seller.pronoun}} shall be liable to pay double the amount of the earnest money (biana) to the SECOND PARTY. In case the SECOND PARTY backs out, the earnest money (biana) shall stand forfeited in favour of the FIRST PARTY.
{{?tds.figures}}7. That the SECOND PARTY shall deduct TDS at 1% ({{tds.figures}}) under section 194-IA of the Income Tax Act and deposit it with the government, and give the challan to the FIRST PARTY.{{/tds.figures}}
{{#terms}}{{.text}}{{/terms}}
That all the terms and conditions laid down in this AGREEMENT TO SELL have been read and duly explained, and after hearing and considering the same, the parties to the agreement have signed this AGREEMENT TO SELL and treat the contents hereof to be true and correct.
IN WITNESS WHEREOF the FIRST PARTY and the SECOND PARTY have set their hands on this AGREEMENT TO SELL on {{execution.date}} in the presence of the following witnesses.
{{#witnesses}}WITNESS {{.n}}: {{.name}}{{?.address}}, {{.address}}{{/.address}}{{/witnesses}}

FIRST PARTY (SELLER): {{sellers.names}}

SECOND PARTY (BUYER): {{buyers.names}}
""";

    private const string FormatB = """
E-stamp Certificate No. __________ dated __________
# AGREEMENT TO SELL
This agreement to sell is made at {{execution.place}} today the {{execution.date}} between the following:-
1. {{sellers.text}} (hereinafter referred to as the Sellers, which term shall where the context so admits include their heirs, executors, successors, assignees, legal representatives and administrators) of the one part of this deed.
AND
2. {{buyers.text}} (hereinafter referred to as the Purchasers, which term shall where the context so admits include their heirs, executors, successors, assignees, legal representatives and administrators) of the other part of this deed.
WHEREAS the above said Sellers{{?title.note}}, by virtue of {{title.note}},{{/title.note}} are the absolute and undisputed owners and in possession of {{property.description}} with all proprietary rights.
WHEREAS the above said property is free from all sorts of encumbrances, litigation, claims, charges, mortgage, sale, transfer etc. till date.
AND WHEREAS the said Sellers are interested to sell the said property, including all its rights, right of way, internal and external rights, right to water, electricity, air and light, all connections and all deposited securities, in favour of the Purchasers, and the Purchasers have reviewed the documents of the said property and are interested in purchasing the same, and both the parties have agreed on the following terms and conditions:-
1. That the total sale consideration of the said property has been agreed as {{price.figures}} ({{price.words}}).
2. That the said Purchasers have paid the following sums to the said Sellers:
{{#payments}}{{.n}}. {{.mode}}{{?.reference}} {{.reference}}{{/.reference}} — {{.date}}{{?.bank}} — {{.bank}}{{/.bank}} — {{.figures}}{{/payments}}
Total paid: {{paid.figures}} ({{paid.words}}).
{{?earnest.percent}}3. The EARNEST MONEY (BIANA) will be {{earnest.percent}}% of the agreed consideration, i.e. {{earnest.figures}}, as per the terms of this agreement.{{/earnest.percent}}
4. That the balance amount out of the total sale consideration shall be paid by the Purchasers to the said Sellers as per the following details by the dates mentioned herein:
{{#schedule}}{{.n}}. {{.mode}} — by {{.date}} — {{.figures}}{{/schedule}}
Total balance: {{balance.figures}} ({{balance.words}}).
5. That the last date for the final payment and the execution and registration of the transfer papers has been fixed on or before {{final.date}}.
6. That the said Sellers shall pay up to date dues till the date the new allotment is issued by the builder of the property and shall get the property transferred in favour of the Purchasers or their nominee. The Purchasers shall hand over their requisite affidavits and indemnity bonds to the Sellers in time to enable the Sellers to submit the transfer papers.
7. That the said Sellers shall hand over physical and vacant possession of the said property along with all papers and documents relating to it to the said Purchasers upon collecting the full consideration listed above.
8. That the said Purchasers shall have the right to get the transfer of the said property executed in their own names or in the name of their nominee(s), to which the said Sellers shall have no objection.
{{?tds.figures}}9. That the Purchasers shall deduct 1% TDS ({{tds.figures}}) on the sale consideration under section 194-IA of the Income Tax Act and deposit it with the government, and submit the challan as proof before the execution of the documents.{{/tds.figures}}
10. That the property under sale is not subject to any previous agreement to sell.
11. Dispute, if any, shall be settled amicably between the parties; failing which, it shall be triable by the court of competent jurisdiction.
{{#terms}}{{.text}}{{/terms}}
IN WITNESS WHEREOF, both the parties have put their hands on this deed on the day, month and year first above mentioned in the presence of the witnesses, with their free consent and without any undue pressure, fraud or misrepresentation.
{{#witnesses}}WITNESS {{.n}}: {{.name}}{{?.address}}, {{.address}}{{/.address}}{{/witnesses}}

SELLERS: {{sellers.names}}

PURCHASERS: {{buyers.names}}
""";

    private const string TokenReceipt = """
# RECEIPT OF EARNEST MONEY (BIANA)
Received a sum of {{paid.figures}} ({{paid.words}}) as earnest money (biana) towards the sale price of {{price.figures}} ({{price.words}}) of {{property.description}}, from {{buyers.names}}, which {{seller.pronoun}} {{seller.has}} agreed to sell to them as per the terms and conditions of the agreement to sell, as under:
{{#payments}}{{.mode}} of {{.figures}}{{?.reference}} — {{.reference}}{{/.reference}}{{?.bank}} — {{.bank}}{{/.bank}} — {{.date}}{{/payments}}
The balance of {{balance.figures}} ({{balance.words}}) is to be paid on or before {{final.date}}.
Dated: {{execution.date}}   Place: {{execution.place}}
{{#witnesses}}WITNESS {{.n}}: {{.name}}{{/witnesses}}

RECEIVED BY (SELLER): {{sellers.names}}
""";

    private const string Rent = """
# RENT AGREEMENT
This rent agreement is made at {{execution.place}} on {{execution.date}} between {{sellers.text}} (hereinafter called the LANDLORD) and {{buyers.text}} (hereinafter called the TENANT).
WHEREAS the LANDLORD is the owner of {{property.description}} and has agreed to let it out to the TENANT on the following terms:
1. The tenancy shall be for a period of eleven (11) months commencing from {{execution.date}}.
2. The monthly rent shall be {{price.figures}} ({{price.words}}), payable in advance on or before the 7th day of each month.
{{?paid.figures}}3. The TENANT has paid {{paid.figures}} ({{paid.words}}) as interest-free security deposit, refundable at the end of the tenancy after adjusting any dues or damage.{{/paid.figures}}
4. Electricity, water and maintenance charges shall be paid by the TENANT as per actual use.
5. The TENANT shall use the premises for residential purposes only and shall not sublet it.
6. Either party may end this agreement by giving one month's notice in writing.
7. The TENANT shall keep the premises in good condition and hand them over in the same condition, normal wear and tear excepted.
{{#terms}}{{.text}}{{/terms}}
IN WITNESS WHEREOF the parties have signed this agreement on the date first written above.
{{#witnesses}}WITNESS {{.n}}: {{.name}}{{?.address}}, {{.address}}{{/.address}}{{/witnesses}}

LANDLORD: {{sellers.names}}

TENANT: {{buyers.names}}
""";
}
