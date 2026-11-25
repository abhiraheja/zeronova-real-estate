using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNovaRealEstate.Application.Features.Deals;
using ZeroNovaRealEstate.Application.Features.Parties;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Videos;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Deals;
using ZeroNovaRealEstate.Models.Events;
using ZeroNovaRealEstate.Models.Parties;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Videos;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// The staff bells added for notif-lead (N3): a deal moving stage (ESTATE_DEAL_STAGE_CHANGED) and a listing's video that
/// zeronova-media could not prepare (ESTATE_VIDEO_FAILED) — who hears, with which link and category, and when nobody does.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class StaffBellTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Agent = "agent-1";
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345")));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<PropertyDto> Flat() => host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, Price = 9500000m,
        SuperArea = new AreaInput(1650, AreaUnit.SqFt),
    }));

    private async Task<DealDto> Deal(PropertyDto flat, string agent = Agent)
    {
        var buyer = await host.SendAs(CompanyA, new SavePartyCommand(null, new PartyInput { Name = "Sunita Rani", Phone = "9812345678" }));
        return await host.SendAs(CompanyA, new SaveDealCommand(null, new DealInput { PropertyId = flat.Id, BuyerIds = [buyer.Id], AgentId = agent, Price = 9500000m }));
    }

    private DispatchEventMessage[] Bells(string type) => [.. host.Bells.Sent.Where(b => b.Type == type)];

    // ── ESTATE_DEAL_STAGE_CHANGED ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_moving_an_agents_deal_rings_the_agent_with_the_deal_page()
    {
        var flat = await Flat();
        var deal = await Deal(flat);
        host.Bells.Sent.Clear();

        await host.SendAs(CompanyA, new SetDealStageCommand(deal.Id, new DealStageInput(DealStage.Token, null)));   // the dealer, user-test

        var bell = Assert.Single(Bells(BellTypes.DealStageChanged));
        Assert.Equal(CompanyA, bell.CompanyId);
        Assert.Equal([Agent], bell.Notification.Recipients);
        Assert.Null(bell.Notification.Audience);
        Assert.Equal(($"Deal {deal.Code} has the token", $"{flat.Code}: {flat.Title}"), (bell.Notification.Title, bell.Notification.Body));
        Assert.Equal(($"/real-estate/deals/{deal.Id}", $"/real-estate/deals/{deal.Id}", "REAL_ESTATE", "NORMAL", "REAL_ESTATE"),
            (bell.Notification.Link, bell.Notification.Url, bell.Notification.Category, bell.Notification.Priority, bell.Notification.Product));
        Assert.Null(bell.Notification.GroupKey);
    }

    [Fact]
    public async Task An_agent_moving_their_own_deal_rings_the_deal_managers_not_themselves()
    {
        var flat = await Flat();
        var deal = await Deal(flat);
        host.Bells.Sent.Clear();

        await host.SendAsMember(CompanyA, Agent, [EstatePermissions.DealManage], new SetDealStageCommand(deal.Id, new DealStageInput(DealStage.Offer, null)));
        Assert.Empty(Bells(BellTypes.DealStageChanged));   // no change of stage: no bell

        await host.SendAsMember(CompanyA, Agent, [EstatePermissions.DealManage],
            new SetDealStageCommand(deal.Id, new DealStageInput(DealStage.Cancelled, "Buyer's loan was refused")));
        var bell = Assert.Single(Bells(BellTypes.DealStageChanged));
        Assert.Empty(bell.Notification.Recipients);
        Assert.DoesNotContain(Agent, bell.Notification.Audience!.UserIds ?? []);
        Assert.Equal([EstatePermissions.DealView], bell.Notification.Audience.PermissionCodes!);
        Assert.False(bell.Notification.Audience.CompanyAdmins);
        Assert.Equal(($"Deal {deal.Code} fell through", $"{flat.Code}: {flat.Title} — Buyer's loan was refused"), (bell.Notification.Title, bell.Notification.Body));
    }

    [Fact]
    public async Task Completing_a_deal_also_rings_the_agents_of_the_offers_it_cancelled()
    {
        var flat = await Flat();
        var won = await Deal(flat, "agent-1");
        var lost = await Deal(flat, "agent-2");
        host.Bells.Sent.Clear();

        await host.SendAs(CompanyA, new SetDealStageCommand(won.Id, new DealStageInput(DealStage.Completed, null)));

        var bells = Bells(BellTypes.DealStageChanged);
        Assert.Equal(2, bells.Length);
        var toWinner = Assert.Single(bells, b => b.Notification.Link == $"/real-estate/deals/{won.Id}");
        Assert.Equal(["agent-1"], toWinner.Notification.Recipients);
        Assert.Equal($"Deal {won.Code} is done: sold", toWinner.Notification.Title);
        var toLoser = Assert.Single(bells, b => b.Notification.Link == $"/real-estate/deals/{lost.Id}");
        Assert.Equal(["agent-2"], toLoser.Notification.Recipients);
        Assert.Equal($"Deal {lost.Code} fell through", toLoser.Notification.Title);
    }

    // ── ESTATE_VIDEO_FAILED ──────────────────────────────────────────────────────────────────────────────

    private async Task<string> Started(EstateListingKind kind, string listingId, string uploader)
    {
        var perms = new[] { EstatePermissions.PropertyManage, EstatePermissions.PropertyView, EstatePermissions.ProjectManage };
        var upload = await host.SendAsMember(CompanyA, uploader, perms, new StartEstateVideoUploadCommand(kind, listingId, "walkthrough.mp4", 50L << 20, "video/mp4", "Walkthrough"));
        await host.SendAsMember(CompanyA, uploader, perms, new CompleteEstateVideoUploadCommand(upload.VideoId, [new EstateVideoPartInput(1, "\"a\"")]));
        return upload.VideoId;
    }

    private static VideoFailedNotification Failed(string videoId, string externalRef) => new()
    {
        VideoId = videoId, ClientId = "real-estate", AccountId = CompanyA, ExternalRef = externalRef, Reason = EstateVideoRules.TooLong, DurationSeconds = 185,
        At = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task A_failed_video_rings_its_uploader_once_with_the_property_page()
    {
        var flat = await Flat();
        var videoId = await Started(EstateListingKind.Property, flat.Id, "uploader-1");
        host.Bells.Sent.Clear();

        await host.PublishAsync(Failed(videoId, $"property:{flat.Id}"));
        await host.PublishAsync(Failed(videoId, $"property:{flat.Id}"));   // a redelivery rings nothing more

        var bell = Assert.Single(Bells(BellTypes.VideoFailed));
        Assert.Equal(CompanyA, bell.CompanyId);
        Assert.Equal(["uploader-1"], bell.Notification.Recipients);
        Assert.Null(bell.Notification.Audience);
        Assert.Equal(("Video \"Walkthrough\" could not be prepared", EstateVideoRules.TooLong), (bell.Notification.Title, bell.Notification.Body));
        Assert.Equal(($"/real-estate/properties/{flat.Id}", "ALERTS", "NORMAL", "REAL_ESTATE"),
            (bell.Notification.Link, bell.Notification.Category, bell.Notification.Priority, bell.Notification.Product));
    }

    [Fact]
    public async Task A_ready_video_rings_nobody_and_a_project_video_links_the_project()
    {
        var project = await host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
        {
            Name = "Aetro Sapphire", Kind = ProjectKind.Own, Locality = "Sunam Road", City = "Sunam", Categories = [ProjectCategory.Residential],
            Stage = ProjectStage.UnderConstruction, Reras = [new ReraInput("PBRERA-SGR78-PC0365", "Punjab", null, null, null)],
        }));
        var ready = await Started(EstateListingKind.Project, project.Id, "uploader-1");
        var failing = await Started(EstateListingKind.Project, project.Id, "uploader-1");
        host.Bells.Sent.Clear();

        await host.PublishAsync(new VideoReadyNotification
        {
            VideoId = ready, ClientId = "real-estate", AccountId = CompanyA, ExternalRef = $"project:{project.Id}", DurationSeconds = 60, At = DateTimeOffset.UtcNow,
        });
        Assert.Empty(Bells(BellTypes.VideoFailed));

        await host.PublishAsync(Failed(failing, $"project:{project.Id}"));
        Assert.Equal($"/real-estate/projects/{project.Id}", Assert.Single(Bells(BellTypes.VideoFailed)).Notification.Link);
    }

    [Fact]
    public async Task The_uploader_watching_the_poll_is_not_rung_and_another_reader_does_not_stop_it()
    {
        var flat = await Flat();
        var mine = await Started(EstateListingKind.Property, flat.Id, "uploader-1");
        var theirs = await Started(EstateListingKind.Property, flat.Id, "uploader-1");
        host.Media.Finish(mine, "Failed", 185, EstateVideoRules.TooLong);
        host.Media.Finish(theirs, "Failed", 185, EstateVideoRules.TooLong);
        host.Bells.Sent.Clear();

        // The uploader's own screen polls and shows it failed: the only would-be recipient is the one looking — no bell.
        await host.SendAsMember(CompanyA, "uploader-1", [EstatePermissions.PropertyView], new GetEstateVideoStatusQuery([mine]));
        Assert.Empty(Bells(BellTypes.VideoFailed));

        // Someone else's screen polls: the uploader still hears of it.
        await host.SendAsMember(CompanyA, "viewer-2", [EstatePermissions.PropertyView], new GetEstateVideoStatusQuery([theirs]));
        Assert.Equal(["uploader-1"], Assert.Single(Bells(BellTypes.VideoFailed)).Notification.Recipients);
    }

    [Fact]
    public async Task A_video_from_before_uploaders_were_kept_rings_the_property_managers()
    {
        var flat = await Flat();
        var videoId = await Started(EstateListingKind.Property, flat.Id, "uploader-1");
        await host.WithAsync(CompanyA, async sp =>
        {
            var videos = sp.GetRequiredService<IEstateVideoRepository>();
            var row = await videos.GetDocuments(v => v.VideoId == videoId).FirstAsync();
            row.UploadedBy = null;
            await videos.UpdateAsync(row, CancellationToken.None);
        });
        host.Bells.Sent.Clear();

        await host.PublishAsync(Failed(videoId, $"property:{flat.Id}"));

        var bell = Assert.Single(Bells(BellTypes.VideoFailed));
        Assert.Empty(bell.Notification.Recipients);
        Assert.Equal([EstatePermissions.PropertyManage], bell.Notification.Audience!.PermissionCodes!);
    }
}
