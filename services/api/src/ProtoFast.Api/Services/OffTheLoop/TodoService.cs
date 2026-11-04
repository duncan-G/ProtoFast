using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database.Abstractions;
using TodoRecord = ProtoFast.Data.OffTheLoop.Entities.Todo;

namespace ProtoFast.Api.Services.OffTheLoop;

public class TodoService(
    IUnitOfWorkFactory<OffTheLoopDbContext> unitOfWorkFactory,
    ITodoRepository todoRepository,
    IQueryFactory<TodoRecord, ITodoQuery> todoQueries,
    TimeProvider time) : Todos.TodosBase
{
    /// <summary>How long a ticked-off to-do stays on the list.</summary>
    private static readonly TimeSpan DoneShownFor = TimeSpan.FromDays(1);

    public override async Task<ListTodosReply> ListTodos(ListTodosRequest request, ServerCallContext context)
    {
        using var unitOfWork = unitOfWorkFactory.CreateReadOnly(nameof(ListTodos));
        var todos = await todoRepository.GetByQueryAsync(
            todoQueries.Create().OpenOrDoneSince(time.GetUtcNow().UtcDateTime - DoneShownFor).InOrder(),
            context.CancellationToken);

        var reply = new ListTodosReply();
        reply.Todos.AddRange(todos.Select(TodoMessages.ToMessage));
        return reply;
    }

    public override async Task<CreateTodoReply> CreateTodo(CreateTodoRequest request, ServerCallContext context)
    {
        var message = request.Todo ?? throw LoopErrors.Invalid("There’s no to-do to save.");
        var todo = new TodoRecord
        {
            Label = TodoMessages.Label(message),
            DueOn = TodoMessages.DueOn(message),
            DoneAt = message.Done ? time.GetUtcNow().UtcDateTime : null,
        };

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(CreateTodo));
        await todoRepository.AddAsync(todo, context.CancellationToken);
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new CreateTodoReply { Todo = TodoMessages.ToMessage(todo) };
    }

    /// <summary>Ticking keeps the first time it was ticked; unticking clears it.</summary>
    public override async Task<UpdateTodoReply> UpdateTodo(UpdateTodoRequest request, ServerCallContext context)
    {
        var message = request.Todo ?? throw LoopErrors.Invalid("There’s no to-do to save.");
        var todoId = LoopIds.Existing(message.Id, LoopErrors.TodoNotFound);
        var label = TodoMessages.Label(message);
        var dueOn = TodoMessages.DueOn(message);

        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(UpdateTodo));
        var todo = await todoRepository.GetFirstByQueryAsync(todoQueries.Create().WithId(todoId), context.CancellationToken)
                   ?? throw LoopErrors.NotFound(LoopErrors.TodoNotFound);
        todo.Label = label;
        todo.DueOn = dueOn;
        todo.DoneAt = message.Done ? todo.DoneAt ?? time.GetUtcNow().UtcDateTime : null;
        await unitOfWork.CommitAsync(context.CancellationToken);

        return new UpdateTodoReply { Todo = TodoMessages.ToMessage(todo) };
    }

    public override async Task<DeleteTodoReply> DeleteTodo(DeleteTodoRequest request, ServerCallContext context)
    {
        var todoId = LoopIds.Existing(request.TodoId, LoopErrors.TodoNotFound);
        using var unitOfWork = unitOfWorkFactory.CreateReadWrite(nameof(DeleteTodo));
        var deleted = await todoRepository.DeleteByQueryAsync(todoQueries.Create().WithId(todoId), context.CancellationToken);
        if (deleted == 0)
        {
            throw LoopErrors.NotFound(LoopErrors.TodoNotFound);
        }

        await unitOfWork.CommitAsync(context.CancellationToken);
        return new DeleteTodoReply();
    }
}
