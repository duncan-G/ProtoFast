using Grpc.Core;
using ProtoFast.Api.Admin;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

using AdminGetStoryRequest = ProtoFast.Api.Admin.Theplot.GetStoryRequest;
using AdminListStoriesRequest = ProtoFast.Api.Admin.Theplot.ListStoriesRequest;
using GetStoryReply = ProtoFast.Api.Admin.Theplot.GetStoryReply;
using ListStoriesReply = ProtoFast.Api.Admin.Theplot.ListStoriesReply;

/// <summary>docs/design/admin-consoles.md §7: who may read an app's admin data.</summary>
public sealed class AdminAccessTests(StoryDatabase database)
{
    [Fact]
    public async Task An_operator_with_no_role_is_denied()
    {
        var error = await ListAs(new Operator(database, "operators"));

        Assert.Equal(StatusCode.PermissionDenied, error!.StatusCode);
    }

    [Fact]
    public async Task The_console_role_from_an_apps_own_realm_does_not_count()
    {
        var error = await ListAs(new Operator(database, "theplot", "admin-theplot"));

        Assert.Equal(StatusCode.PermissionDenied, error!.StatusCode);
    }

    [Fact]
    public async Task Another_apps_operator_cannot_list_theplot_data()
    {
        var error = await ListAs(new Operator(database, "operators", "admin-protofast"));

        Assert.Equal(StatusCode.PermissionDenied, error!.StatusCode);
    }

    [Fact]
    public async Task Another_apps_operator_fetching_a_theplot_row_by_id_gets_not_found()
    {
        var story = await new Writer(database).CreateStoryAsync();
        var outsider = new Operator(database, "operators", "admin-protofast", "platform");

        var error = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotAdminService, GetStoryReply>(
            (s, c) => s.GetStory(new AdminGetStoryRequest { StoryId = story.Id }, c)));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Platform_alone_cannot_read_an_apps_data()
    {
        var platform = new Operator(database, "operators", "platform");

        var listError = await ListAs(platform);
        var overviewError = await Assert.ThrowsAsync<RpcException>(() => platform.Call<AdminOverviewService, GetOverviewReply>(
            (s, c) => s.GetOverview(new GetOverviewRequest { App = "theplot" }, c)));

        Assert.Equal(StatusCode.PermissionDenied, listError!.StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, overviewError.StatusCode);
    }

    [Fact]
    public async Task Theplots_operator_reads_every_writers_stories()
    {
        var first = await new Writer(database).CreateStoryAsync("First writer's story");
        var second = await new Writer(database).CreateStoryAsync("Second writer's story");
        var theplot = new Operator(database, "operators", "admin-theplot");

        var listed = await theplot.Call<TheplotAdminService, ListStoriesReply>(
            (s, c) => s.ListStories(new AdminListStoriesRequest { PageSize = 100 }, c));
        var fetched = await theplot.Call<TheplotAdminService, GetStoryReply>(
            (s, c) => s.GetStory(new AdminGetStoryRequest { StoryId = second.Id }, c));

        Assert.Contains(listed.Stories, s => s.Id == first.Id);
        Assert.Contains(listed.Stories, s => s.Id == second.Id);
        Assert.Equal("Second writer's story", fetched.Story.Title);
        Assert.Equal(1, fetched.Story.SceneCount);
        Assert.NotEqual(listed.Stories.Single(s => s.Id == first.Id).OwnerId, fetched.Story.OwnerId);
    }

    [Fact]
    public async Task Theplots_operator_sees_theplots_overview_and_nothing_for_another_app()
    {
        await new Writer(database).CreateStoryAsync();
        var theplot = new Operator(database, "operators", "admin-theplot");

        var overview = await theplot.Call<AdminOverviewService, GetOverviewReply>(
            (s, c) => s.GetOverview(new GetOverviewRequest { App = "theplot" }, c));
        var other = await Assert.ThrowsAsync<RpcException>(() => theplot.Call<AdminOverviewService, GetOverviewReply>(
            (s, c) => s.GetOverview(new GetOverviewRequest { App = "protofast" }, c)));

        Assert.True(overview.Metrics.Single(m => m.Label == "Stories").Value >= 1);
        Assert.Equal(StatusCode.PermissionDenied, other.StatusCode);
    }

    private static async Task<RpcException?> ListAs(Operator caller)
    {
        try
        {
            await caller.Call<TheplotAdminService, ListStoriesReply>(
                (s, c) => s.ListStories(new AdminListStoriesRequest(), c));
            return null;
        }
        catch (RpcException ex)
        {
            return ex;
        }
    }
}
