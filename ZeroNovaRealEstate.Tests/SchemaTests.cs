using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using ZeroNovaRealEstate.Api.Installers;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The GraphQL schema, built the way the API builds it. A bad resolver fails the whole schema (the whole service); this
/// also names the fields the app's operations rely on. Set ZN_ESTATE_SDL_OUT to a path to also write the SDL there (the
/// app's codegen reads it).
/// </summary>
public sealed class SchemaTests
{
    [Fact]
    public async Task Schema_builds_with_the_real_estate_fields()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphqlApi(new Env(), new ConfigurationBuilder().Build());

        var executor = await services.BuildServiceProvider().GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        var sdl = executor.Schema.ToString();

        foreach (var field in new[]
        {
            "ZeroNova_estateProperties(", "ZeroNova_estateProperty(", "ZeroNova_estateStaleProperties(", "ZeroNova_estatePropertyDuplicates(",
            "ZeroNova_estateParties(", "ZeroNova_estateParty(", "ZeroNova_estateRequirements(", "ZeroNova_estateRequirement(", "ZeroNova_estateMatches(",
            "ZeroNova_estateSettings", "ZeroNova_estateCustomFields", "ZeroNova_estateLocalities(", "ZeroNova_estateDashboard",
            "ZeroNova_saveEstateProperty(", "ZeroNova_confirmEstateProperty(", "ZeroNova_deleteEstateProperty(", "ZeroNova_setEstatePropertyOwners(",
            "ZeroNova_saveEstateParty(", "ZeroNova_saveEstateRequirement(", "ZeroNova_closeEstateRequirement(", "ZeroNova_updateEstateMatch(",
            "ZeroNova_saveEstateSettings(", "ZeroNova_saveEstateCustomField(", "ZeroNova_deleteEstateCustomField(",
            "ZeroNova_estateVisits(", "ZeroNova_estateShares(", "ZeroNova_estateSharedProperty(", "ZeroNova_estateOwnerCheckDraft(", "ZeroNova_scheduleEstateVisit(", "ZeroNova_rescheduleEstateVisit(", "ZeroNova_recordEstateVisitResult(", "ZeroNova_shareEstateProperty(",
            "ZeroNova_estateDeals(", "ZeroNova_estateDeal(", "ZeroNova_estateAgreementTemplates", "ZeroNova_estateAgreementPreview(", "ZeroNova_saveEstateDeal(", "ZeroNova_setEstateDealStage(", "ZeroNova_updateEstateDealChecklist(", "ZeroNova_addEstateOwnershipRecord(", "ZeroNova_buildEstateAgreement(", "ZeroNova_saveEstateAgreementTemplate(", "ZeroNova_deleteEstateAgreementTemplate(",
            "ZeroNova_estateProjects(", "ZeroNova_estateProject(", "ZeroNova_estateProjectUnits(", "ZeroNova_estateUnitCostSheet(", "ZeroNova_estatePriceListVersions(",
            "ZeroNova_estateProjectShares(", "ZeroNova_estateSharedProject(", "ZeroNova_saveEstateProject(", "ZeroNova_deleteEstateProject(", "ZeroNova_confirmEstateProject(",
            "ZeroNova_saveEstateUnitType(", "ZeroNova_deleteEstateUnitType(", "ZeroNova_generateEstateUnits(", "ZeroNova_saveEstateUnit(", "ZeroNova_setEstateUnitStatus(",
            "ZeroNova_saveEstatePriceList(", "ZeroNova_saveEstatePaymentPlan(", "ZeroNova_deleteEstatePaymentPlan(", "ZeroNova_saveEstateMilestones(",
            "ZeroNova_addEstateConstructionUpdate(", "ZeroNova_deleteEstateConstructionUpdate(", "ZeroNova_shareEstateProject(",
            "ZeroNova_estateWebsiteSettings", "ZeroNova_saveEstateWebsiteSettings(", "ZeroNova_estateWebsite(", "ZeroNova_estateWebsiteProperties(",
            "ZeroNova_estateWebsiteProperty(", "ZeroNova_estateWebsiteProjects(", "ZeroNova_estateWebsiteProject(",
            "showOnWebsite: Boolean", "websiteSlug: String", "FEW_LEFT", "NO_LONGER_AVAILABLE", "EstateWebsitePropertyFilterInput",
            "PER_FLOOR_ABOVE", "ON_AGREEMENT", "FARMHOUSE", "availableInGroup: Int",
            "ZeroNova_estateEnquiries(", "ZeroNova_estateEnquiry(", "ZeroNova_estateEnquiryByRef(", "ZeroNova_estateEnquiryStats(",
            "ZeroNova_enquireEstateShare(", "ZeroNova_addEstateEnquiry(", "ZeroNova_updateEstateEnquiry(",
            "EnquiryFilterInput", "AddEnquiryInput", "UpdateEnquiryInput", "WEBSITE_FORM", "SHARE_PAGE", "WALK_IN", "AI_OFFICE", "REQUIREMENT_ADDED",
            "VISIT_BOOKED", "NOT_AVAILABLE", "UNREACHABLE", "enquiries: EnquiryCountsDto!", "last7Days: Int!", "bySource: [EnquirySourceCountDto!]!",
            "enquiriesToday: Int!", "enquiriesThisWeek: Int!", "openEnquiries: Int!", "enquiryId: String", "wanted: String",
            "BUILDER_FLOOR", "NORTH_EAST", "SQ_YD", "MARLA", "price: Decimal", "sizeSqFt: Decimal",
            "ZeroNova_estateCustomers(", "ZeroNova_estateCustomer(", "EstateCustomerFilterInput", "EstateCustomerPageDto", "EstateCustomerDashboardDto",
            "hasOpenRequirement: Boolean", "highlighted: Boolean!", "unmet: [CustomerUnmetDto!]!", "ownedProperties: [CustomerOwnedPropertyDto!]",
            "deals: [DealDto!]", "tenancies: [TenancyDto!]", "dealsByStage: [CustomerStageCountDto!]", "contactId: String", "leadIds: [String!]!",
            "ZeroNova_estateDemandReport(", "ZeroNova_sendEstateMatchMessage(", "DemandReportDto", "DemandGroupDto", "MatchMessageDto", "WATCHING",
            "BOUGHT_THROUGH_US", "RENTED_ELSEWHERE", "BUDGET_MISMATCH", "closeReasonCode: RequirementCloseReason", "reasonCode: RequirementCloseReason",
            "watchUntil: DateTime", "revived: Boolean!", "messageVia: MatchMessageVia", "WHATS_APP_TEMPLATE", "draftUrl: String", "shareUrl: String!",
            "watchDays: Int", "matchTemplateName: String", "WEBSITE_SEARCH", "ENQUIRY_NOT_AVAILABLE", "BUDGET_BAND",
            "description: String",
        })
            Assert.Contains(field, sdl);

        if (Environment.GetEnvironmentVariable("ZN_ESTATE_SDL_OUT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, ForApp(sdl) + "\n");
    }

    /// <summary>
    /// The SDL the app's codegen reads: the access policies (<c>@authorize</c>, <c>@allowAnonymous</c>) are the service's
    /// business, not the app's, so they are left out — the file then changes only when the API does.
    /// </summary>
    private static string ForApp(string sdl)
    {
        static bool Policy(DirectiveNode d) => d.Name.Value is "authorize" or "allowAnonymous";
        var document = Utf8GraphQLParser.Parse(sdl);
        var definitions = document.Definitions
            .Where(d => d is not DirectiveDefinitionNode { Name.Value: "authorize" or "allowAnonymous" })
            .Select(d => d is ObjectTypeDefinitionNode o
                ? o.WithDirectives([.. o.Directives.Where(x => !Policy(x))]).WithFields([.. o.Fields.Select(f => f.WithDirectives([.. f.Directives.Where(x => !Policy(x))]))])
                : d)
            .ToList();
        return document.WithDefinitions(definitions).ToString(true);
    }

    private sealed class Env : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ZeroNovaRealEstate.Api";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
