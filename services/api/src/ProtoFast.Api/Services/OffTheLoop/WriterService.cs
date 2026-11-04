using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>Streams a model's markdown into the note being edited; the client saves the note.</summary>
public class WriterService(IWritingModel model, ILogger<WriterService> logger) : Writer.WriterBase
{
    public const int MaxInstruction = 2000;

    /// <summary>Trims the request in place, or throws what the call would.</summary>
    public static void Check(WriteRequest request)
    {
        request.Instruction = LoopText.Required(request.Instruction, MaxInstruction, "The instruction", "Say what to write.");
        request.Title = LoopText.Optional(request.Title, OffTheLoopLimits.Name, "The title");
        // The markers add a few characters to a full note.
        LoopText.Markdown(request.Document, OffTheLoopLimits.Body + 64, "The note");
        LoopText.Markdown(request.Selection, OffTheLoopLimits.Body, "The selection");
    }

    public override async Task Write(
        WriteRequest request, IServerStreamWriter<WriteReply> responseStream, ServerCallContext context)
    {
        Check(request);

        try
        {
            await foreach (var text in model.WriteAsync(WriterPrompt.System, WriterPrompt.User(request), context.CancellationToken))
            {
                await responseStream.WriteAsync(new WriteReply { Text = text }, context.CancellationToken);
            }
        }
        catch (Exception e) when (e is not RpcException && !context.CancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "The writing model failed");
            throw new RpcException(new Status(StatusCode.Unavailable, LoopErrors.WriterFailed));
        }
    }
}
