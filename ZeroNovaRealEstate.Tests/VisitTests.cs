using Microsoft.Extensions.DependencyInjection;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Matching;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Requirements;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Requirements;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Visits;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>M2: site visits with ready messages, reminders once, outcomes moving the match; share links that hide the owner and count opens.</summary>
[Collection(MongoCollection.Name)]
public sealed class VisitTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;
    private PropertyDto plot = null!;
    private RequirementDto requirement = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345")));
        plot = await host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, Locality = "Sector 70", UnitNumber = "UNIT-918X", Price = 12000000m, OwnerExpectation = 11000000m,
            PlotArea = new AreaInput(200, AreaUnit.SqYd), KeysWith = KeysWith.Owner, VisitTimings = "After 4 PM",
        }));
        var owner = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Gaurav Mittal", Phone = "9888800000" }));
        await host.SendAs(CompanyA, new SetPropertyOwnersCommand(plot.Id, [new PropertyOwnerInput(owner.Id, null, HoldingKind.RegisteredSaleDeed)]));
        requirement = await host.SendAs(CompanyA, new SaveRequirementCommand(null, new RequirementInput
        {
            LeadId = "lead-1", LeadName = "Rahul", LeadPhone = "9811122233", Purpose = PropertyPurpose.Sale, Types = [PropertyType.Plot], BudgetMax = 12500000m,
        }));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private VisitInput Visit(DateTimeOffset at) => new()
    {
        PropertyId = plot.Id, RequirementId = requirement.Id, LeadId = "lead-1", LeadName = "Rahul", LeadPhone = "9811122233", At = at, MeetingPoint = "Sector 70 market gate",
    };

    [Fact]
    public async Task A_visit_comes_with_messages_for_the_customer_and_the_owner()
    {
        var at = new DateTimeOffset(2030, 10, 28, 12, 0, 0, TimeSpan.Zero);   // 5:30 PM in India
        var visit = await host.SendAs(CompanyA, new ScheduleVisitCommand(Visit(at)));

        Assert.Equal((VisitStatus.Scheduled, KeysWith.Owner, "After 4 PM", "919888800000"), (visit.Status, visit.KeysWith, visit.VisitTimings, visit.OwnerPhone));
        Assert.Equal("Namaste Rahul, your visit to see the 200 sq yd Plot in Sector 70 is fixed for Mon 28 Oct, 5:30 PM. We meet at Sector 70 market gate. — Sharma Properties",
            visit.ClientMessage);
        Assert.StartsWith("Namaste Gaurav Mittal, a customer will visit", visit.OwnerMessage);

        var agent = await host.SendAsMember(CompanyA, "user-test", [EstatePermissions.RequirementManage], new GetVisitsQuery(null, null, plot.Id, null, null));
        Assert.Null(Assert.Single(agent).OwnerMessage);   // without POWV: no owner number, no owner message
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new ScheduleVisitCommand(Visit(DateTimeOffset.UtcNow.AddDays(-1)))));
    }

    [Fact]
    public async Task The_agent_is_reminded_once_within_the_hour()
    {
        await host.SendAs(CompanyA, new ScheduleVisitCommand(Visit(DateTimeOffset.UtcNow.AddMinutes(40))));
        await host.SendAs(CompanyA, new ScheduleVisitCommand(Visit(DateTimeOffset.UtcNow.AddHours(5))));
        host.Bells.Sent.Clear();

        await host.RunAsJobAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<VisitReminders>().RunOnceAsync(CancellationToken.None)));
        await host.RunAsJobAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<VisitReminders>().RunOnceAsync(CancellationToken.None)));
        Assert.StartsWith("Site visit at", Assert.Single(host.Bells.Sent).Notification.Title);
    }

    [Fact]
    public async Task The_visits_outcome_moves_the_customers_match()
    {
        var visit = await host.SendAs(CompanyA, new ScheduleVisitCommand(Visit(DateTimeOffset.UtcNow.AddDays(1))));
        await host.SendAs(CompanyA, new RecordVisitResultCommand(visit.Id, new VisitResultInput(VisitStatus.Done, VisitOutcome.NotInterested, "Road too narrow")));

        var match = Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(plot.Id, requirement.Id, null)));
        Assert.Equal((MatchStatus.Rejected, "Road too narrow"), (match.Status, match.RejectReason));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new RescheduleVisitCommand(visit.Id, DateTimeOffset.UtcNow.AddDays(2))));
    }

    [Fact]
    public async Task A_shared_page_hides_the_owner_counts_opens_and_marks_the_match_shown()
    {
        var share = await host.SendAs(CompanyA, new CreateShareCommand(plot.Id, requirement.Id));
        Assert.StartsWith("https://app.zeronova.in/p/", share.Url);
        Assert.Contains("Photos and details: " + share.Url, share.Message);
        Assert.Equal(MatchStatus.Shown, Assert.Single(await host.SendAs(CompanyA, new GetMatchesQuery(plot.Id, requirement.Id, null))).Status);

        SharedPropertyDto page = null!;
        await host.RunAsJobAsync(async sp => page = await sp.GetRequiredService<ZeroNova_Packages.MediatR.Interfaces.IMediator>().Send(new GetSharedPropertyQuery(share.Token)));
        await host.RunAsJobAsync(async sp => await sp.GetRequiredService<ZeroNova_Packages.MediatR.Interfaces.IMediator>().Send(new GetSharedPropertyQuery(share.Token)));
        Assert.Equal(("200 sq yd Plot in Sector 70", "Sharma Properties", "PBRERA-SAS79-REA0123", true), (page.Title, page.DealerName, page.ReraNumber, page.StillAvailable));
        Assert.DoesNotContain("UNIT-918X", System.Text.Json.JsonSerializer.Serialize(page));   // never the unit number or the owner
        Assert.DoesNotContain("11000000", System.Text.Json.JsonSerializer.Serialize(page));

        Assert.Equal(2, Assert.Single(await host.SendAs(CompanyA, new GetSharesQuery(plot.Id))).Opens);
        await Assert.ThrowsAsync<ValidatorException>(() => host.RunAsJobAsync(sp =>
            sp.GetRequiredService<ZeroNova_Packages.MediatR.Interfaces.IMediator>().Send(new GetSharedPropertyQuery("nope"))));
    }

    [Fact]
    public async Task The_owner_check_is_a_ready_whatsapp_message()
    {
        var draft = await host.SendAs(CompanyA, new GetOwnerCheckDraftQuery(plot.Id));
        Assert.Equal("919888800000", draft.Phone);
        Assert.Contains("still available? Please reply: 1 = Yes", draft.Text);
        Assert.StartsWith("https://wa.me/919888800000?text=", draft.Link);
    }
}
