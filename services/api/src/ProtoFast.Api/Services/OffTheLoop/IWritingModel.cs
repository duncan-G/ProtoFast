namespace ProtoFast.Api.Services.OffTheLoop;

public interface IWritingModel
{
    /// <summary>The reply's text as the model writes it.</summary>
    IAsyncEnumerable<string> WriteAsync(string system, string user, CancellationToken cancellationToken);
}
