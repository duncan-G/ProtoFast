using ProtoFast.Api.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class TodoTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);

    [Fact]
    public async Task To_dos_list_soonest_due_first_and_undated_last()
    {
        await _seeker.CreateTodoAsync("Reread: rate limiter");
        await _seeker.CreateTodoAsync("Ask Elliot for the equity policy", "2026-11-05");
        await _seeker.CreateTodoAsync("Thank Sofia for the Northwind call", "2026-11-03");

        var todos = await _seeker.ListTodosAsync();

        Assert.Equal(
            ["Thank Sofia for the Northwind call", "Ask Elliot for the equity policy", "Reread: rate limiter"],
            todos.Select(t => t.Label));
    }

    [Fact]
    public async Task A_ticked_to_do_stays_listed_until_unticked()
    {
        var todo = await _seeker.CreateTodoAsync("Send resume v4 to Dana", "2026-11-03");

        todo.Done = true;
        await _seeker.Todos((s, c) => s.UpdateTodo(new UpdateTodoRequest { Todo = todo }, c));
        Assert.True(Assert.Single(await _seeker.ListTodosAsync()).Done);

        todo.Done = false;
        await _seeker.Todos((s, c) => s.UpdateTodo(new UpdateTodoRequest { Todo = todo }, c));
        Assert.False(Assert.Single(await _seeker.ListTodosAsync()).Done);
    }
}
