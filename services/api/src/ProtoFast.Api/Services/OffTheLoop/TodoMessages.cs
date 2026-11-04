using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using TodoRecord = ProtoFast.Data.OffTheLoop.Entities.Todo;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class TodoMessages
{
    public static Todo ToMessage(TodoRecord todo)
    {
        var message = new Todo { Id = todo.Id.ToString(), Label = todo.Label, Done = todo.DoneAt is not null };
        if (todo.DueOn is { } dueOn)
        {
            message.DueOn = LoopText.Format(dueOn);
        }

        return message;
    }

    public static string Label(Todo message) =>
        LoopText.Required(message.Label, OffTheLoopLimits.Line, "A to-do", "Say what there is to do.");

    public static DateOnly? DueOn(Todo message) =>
        message.HasDueOn ? LoopText.OptionalDay(message.DueOn, "The due day") : null;
}
