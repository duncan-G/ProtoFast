using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class GoalTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);

    [Fact]
    public async Task Goals_list_in_the_order_they_were_set()
    {
        await _seeker.CreateGoalAsync("salary", "200k", "150k");
        await _seeker.CreateGoalAsync("title", "staff");

        var goals = await _seeker.ListGoalsAsync();

        Assert.Equal(["salary", "title"], goals.Select(g => g.Kind));
        Assert.Equal(("200k", "150k"), (goals[0].Target, goals[0].Current));
        Assert.Equal("", goals[1].Current);
    }

    [Fact]
    public async Task Updating_a_goal_moves_where_it_stands()
    {
        var goal = await _seeker.CreateGoalAsync("salary", "200k", "150k");

        goal.Current = "185k";
        await _seeker.Goals((s, c) => s.UpdateGoal(new UpdateGoalRequest { Goal = goal }, c));

        Assert.Equal("185k", Assert.Single(await _seeker.ListGoalsAsync()).Current);
    }

    [Fact]
    public async Task A_goal_needs_a_type_and_a_target()
    {
        var untyped = await Assert.ThrowsAsync<RpcException>(() => _seeker.CreateGoalAsync(" ", "200k"));
        var aimless = await Assert.ThrowsAsync<RpcException>(() => _seeker.CreateGoalAsync("salary", ""));

        Assert.Equal(StatusCode.InvalidArgument, untyped.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, aimless.StatusCode);
        Assert.Empty(await _seeker.ListGoalsAsync());
    }
}
