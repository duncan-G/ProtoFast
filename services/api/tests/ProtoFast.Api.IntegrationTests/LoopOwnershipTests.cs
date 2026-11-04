using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Seeker;

namespace ProtoFast.Api.IntegrationTests;

public class LoopOwnershipTests(StoryDatabase database)
{
    private readonly Seeker _owner = new(database);
    private readonly Seeker _stranger = new(database);

    [Fact]
    public async Task Another_users_companies_applications_notes_to_dos_and_goals_read_and_write_as_not_found()
    {
        var company = await _owner.CreateCompanyAsync("Corvid Games");
        var application = await _owner.CreateApplicationAsync(NewApplication(company.Id, Stage.Offer));
        var note = await _owner.CreateNoteAsync(new Note { Kind = NoteKind.Design, Title = "Rate limiter" });
        var todo = await _owner.CreateTodoAsync("Ask Elliot for the equity policy");
        var goal = await _owner.CreateGoalAsync("salary", "200k", "150k");

        Task[] attempts =
        [
            _stranger.UpdateCompanyAsync(company),
            _stranger.Companies((s, c) => s.DeleteCompany(new DeleteCompanyRequest { CompanyId = company.Id }, c)),
            _stranger.UpdateApplicationAsync(application),
            _stranger.Applications((s, c) => s.DeleteApplication(new DeleteApplicationRequest { ApplicationId = application.Id }, c)),
            _stranger.Notes((s, c) => s.UpdateNote(new UpdateNoteRequest { Note = note }, c)),
            _stranger.Notes((s, c) => s.DeleteNote(new DeleteNoteRequest { NoteId = note.Id }, c)),
            _stranger.Todos((s, c) => s.UpdateTodo(new UpdateTodoRequest { Todo = todo }, c)),
            _stranger.Todos((s, c) => s.DeleteTodo(new DeleteTodoRequest { TodoId = todo.Id }, c)),
            _stranger.Goals((s, c) => s.UpdateGoal(new UpdateGoalRequest { Goal = goal }, c)),
            _stranger.Goals((s, c) => s.DeleteGoal(new DeleteGoalRequest { GoalId = goal.Id }, c)),
        ];

        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => attempt);
            Assert.Equal(StatusCode.NotFound, error.StatusCode);
        }

        Assert.Empty(await _stranger.ListCompaniesAsync());
        Assert.Empty(await _stranger.ListApplicationsAsync());
        Assert.Empty(await _stranger.ListNotesAsync());
        Assert.Empty(await _stranger.ListTodosAsync());
        Assert.Empty(await _stranger.ListGoalsAsync());
        Assert.Single(await _owner.ListCompaniesAsync());
        Assert.Single(await _owner.ListApplicationsAsync());
        Assert.Single(await _owner.ListNotesAsync());
        Assert.Single(await _owner.ListTodosAsync());
        Assert.Single(await _owner.ListGoalsAsync());
    }
}
