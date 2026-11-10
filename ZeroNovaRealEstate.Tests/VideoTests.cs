using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;
using ZeroNovaRealEstate.Application.Features.Projects;
using ZeroNovaRealEstate.Application.Features.Properties;
using ZeroNovaRealEstate.Application.Features.Settings;
using ZeroNovaRealEstate.Application.Features.Videos;
using ZeroNovaRealEstate.Application.Features.Visits;
using ZeroNovaRealEstate.Application.Features.Website;
using ZeroNovaRealEstate.Application.Interfaces;
using ZeroNovaRealEstate.Domain.Documents;
using ZeroNovaRealEstate.Models.Constants;
using ZeroNovaRealEstate.Models.Projects;
using ZeroNovaRealEstate.Models.Properties;
using ZeroNovaRealEstate.Models.Settings;
using ZeroNovaRealEstate.Models.Videos;
using ZeroNovaRealEstate.Models.Website;
using static ZeroNovaRealEstate.Tests.TestHost;

namespace ZeroNovaRealEstate.Tests;

/// <summary>
/// Property and project videos (C4, D36) through zeronova-media (a fake): the upload flow and its checks, media's
/// video-ready / video-failed events (this client's only, matched by video and external ref, idempotent and in order),
/// who may change which videos, playback grants for the app, share links and the website only for ready videos of a
/// listing shown there, at most 3 a listing, deletes reaching media, and the service without a media key.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class VideoTests(MongoFixture mongo) : IAsyncLifetime
{
    private TestHost host = null!;

    public async Task InitializeAsync()
    {
        host = new TestHost(mongo);
        await host.EnsureIndexesAsync();
        await host.SendAs(CompanyA, new SaveEstateSettingsCommand(new EstateSettingsInput(272.25m, 5445m, 27225m, 1361.25m, 1361.25m, 10, 30, 15, 50, "Mohali",
            "PBRERA-SAS79-REA0123", "Sharma Properties", "9876012345")));
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private const long Mb = 1L << 20;

    private Task<T> Public<T>(IRequest<T> request)
    {
        T result = default!;
        return host.RunAsJobAsync(async sp => result = await sp.GetRequiredService<IMediator>().Send(request)).ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result;
        });
    }

    private Task<PropertyDto> Flat(bool show = true) => host.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
    {
        Purpose = PropertyPurpose.Sale, Type = PropertyType.Flat, City = "Mohali", Locality = "Sector 70", Bhk = 3, Price = 9500000m,
        SuperArea = new AreaInput(1650, AreaUnit.SqFt), ShowOnWebsite = show,
    }));

    private Task<ProjectDto> Project(bool show = true) => host.SendAs(CompanyA, new SaveProjectCommand(null, new ProjectInput
    {
        Name = "Aetro Sapphire", Kind = ProjectKind.Own, Locality = "Sunam Road", City = "Sunam", Categories = [ProjectCategory.Residential],
        Stage = ProjectStage.UnderConstruction, Reras = [new ReraInput("PBRERA-SGR78-PC0365", "Punjab", null, null, null)], ShowOnWebsite = show,
    }));

    private Task<EstateVideoUploadDto> Start(EstateListingKind kind, string listingId, long size = 150 * Mb, string type = "video/mp4", string? title = null) =>
        host.SendAs(CompanyA, new StartEstateVideoUploadCommand(kind, listingId, "walkthrough.mp4", size, type, title));

    /// <summary>Uploads and completes a video, then media finishes it (ready by default) and the poll picks it up.</summary>
    private async Task<string> Uploaded(EstateListingKind kind, string listingId, string status = "Ready", double duration = 95, string? title = null)
    {
        var upload = await Start(kind, listingId, title: title);
        await host.SendAs(CompanyA, new CompleteEstateVideoUploadCommand(upload.VideoId,
            [.. upload.Parts.Select(p => new EstateVideoPartInput(p.Number, $"\"etag{p.Number}\""))]));
        host.Media.Finish(upload.VideoId, status, duration, status == "Failed" ? EstateVideoRules.TooLong : null);
        await host.SendAs(CompanyA, new GetEstateVideoStatusQuery([upload.VideoId]));
        return upload.VideoId;
    }

    private async Task<EstateVideoDocument> Row(string videoId, string company = CompanyA)
    {
        EstateVideoDocument row = null!;
        await host.WithAsync(company, async sp =>
            row = await sp.GetRequiredService<IEstateVideoRepository>().GetDocuments(v => v.VideoId == videoId, true).FirstAsync());
        return row;
    }

    private static VideoReadyNotification Ready(string videoId, string externalRef, double duration = 95, string client = "real-estate", string company = CompanyA,
        DateTimeOffset? at = null) => new()
    {
        VideoId = videoId, ClientId = client, AccountId = company, ExternalRef = externalRef, DurationSeconds = duration, Renditions = ["360p"],
        PosterUrl = $"https://video.test/{videoId}/poster.jpg", At = at ?? DateTimeOffset.UtcNow,
    };

    // ── Upload ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_property_video_is_uploaded_completed_and_ready_with_its_poster_and_length()
    {
        var flat = await Flat();
        var upload = await Start(EstateListingKind.Property, flat.Id, 40 * Mb, "Video/MP4", "Living room");

        var created = Assert.Single(host.Media.Created);
        Assert.Equal((CompanyA, "Living room", $"property:{flat.Id}", 40 * Mb, "video/mp4", 120), created);
        Assert.Equal(3, upload.PartCount);
        Assert.Equal(3, upload.Parts.Count);
        Assert.Equal(16 * Mb, upload.PartSize);
        Assert.Equal(EstateVideoStatus.Uploading, upload.Video.Status);

        var more = await host.SendAs(CompanyA, new MoreEstateVideoPartsCommand(upload.VideoId, [3]));
        Assert.Equal([3], more.Parts.Select(p => p.Number));

        var done = await host.SendAs(CompanyA, new CompleteEstateVideoUploadCommand(upload.VideoId,
            [new EstateVideoPartInput(2, "\"b\""), new EstateVideoPartInput(1, "\"a\""), new EstateVideoPartInput(3, "\"c\"")]));
        Assert.Equal(EstateVideoStatus.Processing, done.Status);
        Assert.Equal(3, Assert.Single(host.Media.Completed).Parts);

        // The app polls until ready.
        host.Media.Finish(upload.VideoId, duration: 118.4);
        var status = Assert.Single(await host.SendAs(CompanyA, new GetEstateVideoStatusQuery([upload.VideoId])));
        Assert.Equal(EstateVideoStatus.Ready, status.Status);
        Assert.Equal(118.4, status.DurationSeconds);
        Assert.Equal($"https://video.test/{upload.VideoId}/poster.jpg", status.PosterUrl);

        var dto = await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id));
        var video = Assert.Single(dto.Videos);
        Assert.Equal(("Living room", EstateVideoStatus.Ready, EstateListingKind.Property, flat.Id), (video.Title, video.Status, video.ListingKind, video.ListingId));

        var renamed = await host.SendAs(CompanyA, new RenameEstateVideoCommand(upload.VideoId, "  Hall and   balcony "));
        Assert.Equal("Hall and balcony", renamed.Title);
        Assert.Equal("Hall and balcony", Assert.Single(host.Media.Renamed).Title);
    }

    [Fact]
    public async Task Uploads_refuse_non_video_files_big_files_units_and_a_fourth_video()
    {
        var flat = await Flat();
        var notVideo = await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Property, flat.Id, type: "image/jpeg"));
        Assert.Contains("video file", notVideo.Message);
        var big = await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Property, flat.Id, size: EstateVideoRules.MaxUploadBytes + 1));
        Assert.Contains("1 GB", big.Message);
        await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Property, "6a00000000000000000000ff"));
        await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Project, flat.Id));
        Assert.Empty(host.Media.Created);

        // A unit shows its project's videos.
        var project = await Project();
        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1250, AreaUnit.SqFt),
        }));
        var unit = await host.SendAs(CompanyA, new SaveUnitCommand(null, new UnitInput { ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "A-101", Floor = 1 }));
        var onUnit = await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Property, unit.Id));
        Assert.Contains("project's videos", onUnit.Message);

        // At most 3 a listing — a failed one counts until deleted.
        await Uploaded(EstateListingKind.Property, flat.Id);
        await Uploaded(EstateListingKind.Property, flat.Id);
        var failed = await Uploaded(EstateListingKind.Property, flat.Id, "Failed", 150);
        var fourth = await Assert.ThrowsAsync<ValidatorException>(() => Start(EstateListingKind.Property, flat.Id));
        Assert.Contains("at most 3", fourth.Message);
        await host.SendAs(CompanyA, new DeleteEstateVideoCommand(failed));
        await Start(EstateListingKind.Property, flat.Id);

        // The order: given ids first, the rest after.
        var ids = (await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Videos.Select(v => v.VideoId).ToList();
        Assert.Equal(3, ids.Count);
        var reordered = await host.SendAs(CompanyA, new ReorderEstateVideosCommand(EstateListingKind.Property, flat.Id, [ids[2], ids[1]]));
        Assert.Equal([ids[2], ids[1], ids[0]], reordered.Select(v => v.VideoId));
        Assert.Equal([ids[2], ids[1], ids[0]], (await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Videos.Select(v => v.VideoId));
    }

    [Fact]
    public async Task An_abandoned_upload_is_failed_by_the_poll_and_cancel_aborts_in_media()
    {
        var flat = await Flat();
        var first = await Start(EstateListingKind.Property, flat.Id);
        host.Media.Videos.TryRemove(first.VideoId, out _);   // media's sweep dropped it
        var polled = Assert.Single(await host.SendAs(CompanyA, new GetEstateVideoStatusQuery([first.VideoId])));
        Assert.Equal(EstateVideoStatus.Failed, polled.Status);
        Assert.Contains("did not finish", polled.FailureReason);

        var second = await Start(EstateListingKind.Property, flat.Id);
        Assert.True(await host.SendAs(CompanyA, new CancelEstateVideoUploadCommand(second.VideoId)));
        Assert.Equal((CompanyA, second.VideoId), Assert.Single(host.Media.Aborted));
        Assert.True((await Row(second.VideoId)).IsDeleted);
        Assert.DoesNotContain((await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Videos, v => v.VideoId == second.VideoId);
    }

    // ── zeronova-media's events ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Video_ready_and_failed_events_update_the_row_once_in_order_and_only_for_this_client()
    {
        var project = await Project();
        var upload = await Start(EstateListingKind.Project, project.Id);
        await host.SendAs(CompanyA, new CompleteEstateVideoUploadCommand(upload.VideoId, [new EstateVideoPartInput(1, "\"a\"")]));
        var externalRef = $"project:{project.Id}";

        // Another client's event, another company's, a wrong ref: nothing.
        await host.PublishAsync(Ready(upload.VideoId, externalRef, client: "study"));
        await host.PublishAsync(Ready(upload.VideoId, externalRef, company: CompanyB));
        await host.PublishAsync(Ready(upload.VideoId, "project:6a00000000000000000000ff"));
        Assert.Equal(EstateVideoStatus.Processing, (await Row(upload.VideoId)).Status);

        var at = DateTimeOffset.UtcNow;
        await host.PublishAsync(Ready(upload.VideoId, externalRef, 61, at: at));
        var row = await Row(upload.VideoId);
        Assert.Equal((EstateVideoStatus.Ready, 61d), (row.Status, row.DurationSeconds));
        Assert.Equal($"https://video.test/{upload.VideoId}/poster.jpg", row.PosterUrl);

        // A redelivery changes nothing; an older failure delivered late is dropped.
        await host.PublishAsync(Ready(upload.VideoId, externalRef, 61, at: at));
        Assert.Equal(row.Version, (await Row(upload.VideoId)).Version);
        await host.PublishAsync(new VideoFailedNotification
        {
            VideoId = upload.VideoId, ClientId = "real-estate", AccountId = CompanyA, ExternalRef = externalRef, Reason = "No video stream.", At = at.AddMinutes(-1),
        });
        Assert.Equal(EstateVideoStatus.Ready, (await Row(upload.VideoId)).Status);
        Assert.Single((await host.SendAs(CompanyA, new GetProjectQuery(project.Id))).Videos, v => v.Status == EstateVideoStatus.Ready);
    }

    [Fact]
    public async Task A_video_longer_than_two_minutes_fails_with_the_reason()
    {
        var flat = await Flat();
        var a = await Start(EstateListingKind.Property, flat.Id);
        await host.SendAs(CompanyA, new CompleteEstateVideoUploadCommand(a.VideoId, [new EstateVideoPartInput(1, "\"a\"")]));
        await host.PublishAsync(new VideoFailedNotification
        {
            VideoId = a.VideoId, ClientId = "real-estate", AccountId = CompanyA, ExternalRef = $"property:{flat.Id}", DurationSeconds = 185.2,
            Reason = EstateVideoRules.TooLong, At = DateTimeOffset.UtcNow,
        });
        var failed = await Row(a.VideoId);
        Assert.Equal((EstateVideoStatus.Failed, EstateVideoRules.TooLong, 185.2), (failed.Status, failed.FailureReason, failed.DurationSeconds));

        // Checked again on ready (D36): a ready event saying it is longer still fails it.
        var b = await Start(EstateListingKind.Property, flat.Id);
        await host.PublishAsync(Ready(b.VideoId, $"property:{flat.Id}", 140));
        Assert.Equal((EstateVideoStatus.Failed, EstateVideoRules.TooLong), ((await Row(b.VideoId)).Status, (await Row(b.VideoId)).FailureReason));
        Assert.DoesNotContain((await host.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Videos, v => v.Status == EstateVideoStatus.Ready);
    }

    // ── Permissions ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_property_video_needs_PPRM_a_project_video_PPJM_and_watching_needs_no_manage()
    {
        var flat = await Flat();
        var project = await Project();
        var onFlat = await Uploaded(EstateListingKind.Property, flat.Id);

        var propertyOnly = new[] { EstatePermissions.PropertyManage };
        var projectOnly = new[] { EstatePermissions.ProjectManage };
        var viewer = new[] { EstatePermissions.PropertyView };

        var e = await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", propertyOnly,
            new StartEstateVideoUploadCommand(EstateListingKind.Project, project.Id, "a.mp4", Mb, "video/mp4", null)));
        Assert.Contains("Manage projects", e.Message);
        await host.SendAsMember(CompanyA, "agent-1", projectOnly, new StartEstateVideoUploadCommand(EstateListingKind.Project, project.Id, "a.mp4", Mb, "video/mp4", null));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-1", projectOnly, new DeleteEstateVideoCommand(onFlat)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAsMember(CompanyA, "agent-2", viewer, new RenameEstateVideoCommand(onFlat, "x")));

        // A viewer plays it (as themselves); another company never sees it.
        var grant = await host.SendAsMember(CompanyA, "agent-2", viewer, new EstateVideoPlaybackCommand(onFlat));
        Assert.StartsWith("https://media.test/", grant.PlaybackUrl);
        Assert.Equal((CompanyA, onFlat, "app:agent-2", 600), host.Media.Grants.Last());
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyB, new EstateVideoPlaybackCommand(onFlat)));
        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyB, new DeleteEstateVideoCommand(onFlat)));
        Assert.Empty(await host.SendAs(CompanyB, new GetEstateVideoStatusQuery([onFlat])));
    }

    // ── Playback: app, share page, website ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Share_links_show_and_play_only_ready_videos_of_their_listing_and_a_unit_plays_its_projects()
    {
        var flat = await Flat();
        var other = await Flat();
        var ready = await Uploaded(EstateListingKind.Property, flat.Id, title: "Walkthrough");
        var processing = (await Start(EstateListingKind.Property, flat.Id)).VideoId;
        var elsewhere = await Uploaded(EstateListingKind.Property, other.Id);

        await Assert.ThrowsAsync<ValidatorException>(() => host.SendAs(CompanyA, new EstateVideoPlaybackCommand(processing)));

        var share = await host.SendAs(CompanyA, new CreateShareCommand(flat.Id, null));
        var page = await Public(new GetSharedPropertyQuery(share.Token));
        var shown = Assert.Single(page.Videos);
        Assert.Equal((ready, "Walkthrough", 95d), (shown.Id, shown.Title, shown.DurationSeconds));
        Assert.NotNull(shown.PosterUrl);

        var grant = await Public(new EstateShareVideoPlaybackCommand(share.Token, ready));
        Assert.Equal(ready, grant.VideoId);
        var viewer = host.Media.Grants.Last().Viewer;
        Assert.StartsWith($"share:{share.Id}:", viewer);
        await Public(new EstateShareVideoPlaybackCommand(share.Token, ready));
        Assert.NotEqual(viewer, host.Media.Grants.Last().Viewer);   // a ref per visit
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateShareVideoPlaybackCommand(share.Token, processing)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateShareVideoPlaybackCommand(share.Token, elsewhere)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateShareVideoPlaybackCommand("not-a-token", ready)));

        // A project's share, and a unit's (its project's videos).
        var project = await Project();
        var projectVideo = await Uploaded(EstateListingKind.Project, project.Id);
        var projectShare = await host.SendAs(CompanyA, new CreateProjectShareCommand(project.Id, null));
        Assert.Equal([projectVideo], (await Public(new GetSharedProjectQuery(projectShare.Token))).Videos.Select(v => v.Id));
        await Public(new EstateShareVideoPlaybackCommand(projectShare.Token, projectVideo));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateShareVideoPlaybackCommand(projectShare.Token, ready)));

        var type = await host.SendAs(CompanyA, new SaveUnitTypeCommand(project.Id, null, new UnitTypeInput
        {
            Name = "3 BHK", Type = PropertyType.Flat, Bhk = 3, SuperArea = new AreaInput(1250, AreaUnit.SqFt),
        }));
        var unit = await host.SendAs(CompanyA, new SaveUnitCommand(null, new UnitInput { ProjectId = project.Id, UnitTypeId = type.Id, UnitNumber = "A-101", Floor = 1 }));
        Assert.Equal([projectVideo], (await host.SendAs(CompanyA, new GetPropertyQuery(unit.Id))).Videos.Select(v => v.VideoId));
        var unitShare = await host.SendAs(CompanyA, new CreateShareCommand(unit.Id, null));
        Assert.Equal([projectVideo], (await Public(new GetSharedPropertyQuery(unitShare.Token))).Videos.Select(v => v.Id));
        await Public(new EstateShareVideoPlaybackCommand(unitShare.Token, projectVideo));

        // A removed property's link plays nothing.
        await host.SendAs(CompanyA, new DeletePropertyCommand(flat.Id));
        Assert.Empty((await Public(new GetSharedPropertyQuery(share.Token))).Videos);
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateShareVideoPlaybackCommand(share.Token, ready)));
    }

    [Fact]
    public async Task The_website_shows_and_plays_ready_videos_of_listed_properties_and_projects_only()
    {
        var code = (await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(true, null)))).Code;
        var listed = await Flat();
        var hidden = await Flat(show: false);
        var onListed = await Uploaded(EstateListingKind.Property, listed.Id);
        var onHidden = await Uploaded(EstateListingKind.Property, hidden.Id);
        await Start(EstateListingKind.Property, listed.Id);   // still uploading: never on the website

        var listPage = await Public(new GetEstateWebsitePropertiesQuery(code, new EstateWebsitePropertyFilterInput(), EstateWebsitePropertySort.Featured, 1, 12));
        Assert.Equal([onListed], Assert.Single(listPage.Items).Videos.Select(v => v.Id));
        var page = await Public(new GetEstateWebsitePropertyQuery(code, listed.WebsiteSlug!));
        Assert.Equal([onListed], page.Property!.Videos.Select(v => v.Id));

        var grant = await Public(new EstateWebsiteVideoPlaybackCommand(code, listed.WebsiteSlug!, onListed));
        Assert.Equal(onListed, grant.VideoId);
        Assert.StartsWith($"web:{code}:", host.Media.Grants.Last().Viewer);
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateWebsiteVideoPlaybackCommand(code, hidden.WebsiteSlug ?? "x", onHidden)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateWebsiteVideoPlaybackCommand(code, listed.WebsiteSlug!, onHidden)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateWebsiteVideoPlaybackCommand("NOPE99", listed.WebsiteSlug!, onListed)));

        var project = await Project();
        var projectVideo = await Uploaded(EstateListingKind.Project, project.Id);
        var projectPage = await Public(new GetEstateWebsiteProjectQuery(code, project.WebsiteSlug!));
        Assert.Equal([projectVideo], projectPage!.Videos.Select(v => v.Id));
        await Public(new EstateWebsiteVideoPlaybackCommand(code, project.WebsiteSlug!, projectVideo));

        // Off the website: no more grants.
        await host.SendAs(CompanyA, new SaveEstateWebsiteSettingsCommand(new EstateWebsiteSettingsInput(false, null)));
        await Assert.ThrowsAsync<ValidatorException>(() => Public(new EstateWebsiteVideoPlaybackCommand(code, listed.WebsiteSlug!, onListed)));
    }

    // ── Deletes ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_video_a_property_or_a_project_deletes_it_in_media_and_a_media_failure_does_not_block()
    {
        var flat = await Flat();
        var one = await Uploaded(EstateListingKind.Property, flat.Id);
        var two = await Uploaded(EstateListingKind.Property, flat.Id);
        var uploading = (await Start(EstateListingKind.Property, flat.Id)).VideoId;

        Assert.True(await host.SendAs(CompanyA, new DeleteEstateVideoCommand(one)));
        Assert.Contains((CompanyA, one), host.Media.Deleted);

        await host.SendAs(CompanyA, new DeletePropertyCommand(flat.Id));
        Assert.Contains((CompanyA, two), host.Media.Deleted);
        Assert.Contains((CompanyA, uploading), host.Media.Aborted);
        Assert.True((await Row(two)).IsDeleted);
        Assert.True((await Row(uploading)).IsDeleted);

        var project = await Project();
        var onProject = await Uploaded(EstateListingKind.Project, project.Id);
        host.Media.FailDeletes = true;
        Assert.True(await host.SendAs(CompanyA, new DeleteProjectCommand(project.Id)));
        Assert.True((await Row(onProject)).IsDeleted);
        Assert.DoesNotContain((CompanyA, onProject), host.Media.Deleted);
    }

    // ── Without a media key ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_a_media_key_the_service_works_and_only_video_operations_are_refused()
    {
        await using var bare = new TestHost(mongo, realMedia: true);
        await bare.EnsureIndexesAsync();
        var flat = await bare.SendAs(CompanyA, new SavePropertyCommand(null, new PropertyInput
        {
            Purpose = PropertyPurpose.Sale, Type = PropertyType.Plot, City = "Mohali", Locality = "Sector 82", PlotArea = new AreaInput(200, AreaUnit.SqYd), Price = 1m,
        }));
        Assert.Empty((await bare.SendAs(CompanyA, new GetPropertyQuery(flat.Id))).Videos);
        var e = await Assert.ThrowsAsync<ValidatorException>(() =>
            bare.SendAs(CompanyA, new StartEstateVideoUploadCommand(EstateListingKind.Property, flat.Id, "a.mp4", Mb, "video/mp4", null)));
        Assert.Equal("Video is not set up yet.", e.Message);
        Assert.Empty(await bare.SendAs(CompanyA, new GetEstateVideoStatusQuery(["vid001"])));
        Assert.True(await bare.SendAs(CompanyA, new DeletePropertyCommand(flat.Id)));
    }
}
