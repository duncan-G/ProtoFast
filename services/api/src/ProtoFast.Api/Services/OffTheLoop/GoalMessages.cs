using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using GoalRecord = ProtoFast.Data.OffTheLoop.Entities.Goal;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class GoalMessages
{
    public static Goal ToMessage(GoalRecord goal) =>
        new() { Id = goal.Id.ToString(), Kind = goal.Kind, Target = goal.Target, Current = goal.Current };

    public static string Kind(Goal message) =>
        LoopText.Required(message.Kind, OffTheLoopLimits.Label, "A goal’s type", "Say what the goal is for.");

    public static string Target(Goal message) =>
        LoopText.Required(message.Target, OffTheLoopLimits.Label, "A goal", "Say what you’re aiming for.");

    public static string Current(Goal message) =>
        LoopText.Optional(message.Current, OffTheLoopLimits.Label, "Where you are now");
}
