using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using GoalRecord = ProtoFast.Data.OffTheLoop.Entities.Goal;

namespace ProtoFast.Api.Services.OffTheLoop;

public class GoalService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    IGoalRepository goalRepository,
    IQueryFactory<GoalRecord, IGoalQuery> goalQueries) : Goals.GoalsBase
{
    public override async Task<ListGoalsReply> ListGoals(ListGoalsRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListGoals));
        var goals = await goalRepository.GetByQueryAsync(goalQueries.Create().InOrder(), context.CancellationToken);

        var reply = new ListGoalsReply();
        reply.Goals.AddRange(goals.Select(GoalMessages.ToMessage));
        return reply;
    }

    public override async Task<CreateGoalReply> CreateGoal(CreateGoalRequest request, ServerCallContext context)
    {
        var message = request.Goal ?? throw LoopErrors.Invalid("There’s no goal to save.");
        var goal = new GoalRecord
        {
            Kind = GoalMessages.Kind(message),
            Target = GoalMessages.Target(message),
            Current = GoalMessages.Current(message),
        };

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateGoal));
        await goalRepository.AddAsync(goal, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateGoalReply { Goal = GoalMessages.ToMessage(goal) };
    }

    public override async Task<UpdateGoalReply> UpdateGoal(UpdateGoalRequest request, ServerCallContext context)
    {
        var message = request.Goal ?? throw LoopErrors.Invalid("There’s no goal to save.");
        var goalId = LoopIds.Existing(message.Id, LoopErrors.GoalNotFound);
        var kind = GoalMessages.Kind(message);
        var target = GoalMessages.Target(message);
        var current = GoalMessages.Current(message);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateGoal));
        var goal = await goalRepository.GetFirstByQueryAsync(goalQueries.Create().WithId(goalId), context.CancellationToken)
                   ?? throw LoopErrors.NotFound(LoopErrors.GoalNotFound);
        goal.Kind = kind;
        goal.Target = target;
        goal.Current = current;
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new UpdateGoalReply { Goal = GoalMessages.ToMessage(goal) };
    }

    public override async Task<DeleteGoalReply> DeleteGoal(DeleteGoalRequest request, ServerCallContext context)
    {
        var goalId = LoopIds.Existing(request.GoalId, LoopErrors.GoalNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteGoal));
        var deleted = await goalRepository.DeleteByQueryAsync(goalQueries.Create().WithId(goalId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.GoalNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteGoalReply();
    }
}
