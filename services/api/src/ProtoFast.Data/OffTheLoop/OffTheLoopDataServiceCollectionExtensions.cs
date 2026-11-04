using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Data.OffTheLoop.Queries;
using ProtoFast.Data.OffTheLoop.Repositories;
using ProtoFast.Database;

namespace ProtoFast.Data.OffTheLoop;

public static class OffTheLoopDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OffTheLoopDbContext"/> over the ambient <c>NpgsqlDataSource</c>, its
    /// <c>IUnitOfWorkFactory&lt;OffTheLoopDbContext&gt;</c>, and its query factories and repositories.
    /// </summary>
    public static IServiceCollection AddOffTheLoopData(
        this IServiceCollection services,
        Action<DatabaseOptions>? configureOptions = null)
    {
        services.AddCoreDatabaseServices<OffTheLoopDbContext>(configureOptions ?? (_ => { }));

        services.AddQueryFactory<Company, ICompanyQuery, CompanyQuery>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();

        services.AddQueryFactory<Application, IApplicationQuery, ApplicationQuery>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();

        services.AddQueryFactory<Note, INoteQuery, NoteQuery>();
        services.AddScoped<INoteRepository, NoteRepository>();

        services.AddQueryFactory<Todo, ITodoQuery, TodoQuery>();
        services.AddScoped<ITodoRepository, TodoRepository>();

        services.AddQueryFactory<Goal, IGoalQuery, GoalQuery>();
        services.AddScoped<IGoalRepository, GoalRepository>();

        services.AddQueryFactory<Document, IDocumentQuery, DocumentQuery>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        services.AddQueryFactory<ModelRun, IModelRunQuery, ModelRunQuery>();
        services.AddScoped<IModelRunRepository, ModelRunRepository>();

        services.AddQueryFactory<Resume, IResumeQuery, ResumeQuery>();
        services.AddScoped<IResumeRepository, ResumeRepository>();

        return services;
    }
}
