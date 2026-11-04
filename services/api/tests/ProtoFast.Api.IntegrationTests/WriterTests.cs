using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class WriterTests
{
    [Fact]
    public async Task The_reply_streams_as_the_model_writes_it()
    {
        var model = new ScriptedWritingModel("Keep indices ", "on a stack, $O(n)$.");
        var replies = new RecordingStreamWriter<WriteReply>();

        await Write(model, new WriteRequest
        {
            Instruction = "Explain the move",
            Title = "Monotonic <stack>",
            Kind = NoteKind.Pattern,
            Document = "## The move\n\n⟦selection⟧TODO⟦/selection⟧",
            Selection = "TODO",
        }, replies);

        Assert.Equal(["Keep indices ", "on a stack, $O(n)$."], replies.Messages.Select(r => r.Text));
        Assert.Contains("<note title=\"Monotonic &lt;stack&gt;\" kind=\"pattern\">\n## The move", model.User);
        Assert.Contains("<selection>\nTODO\n</selection>", model.User);
        Assert.EndsWith("<instruction>\nExplain the move\n</instruction>", model.User);
    }

    [Fact]
    public async Task An_instruction_is_required()
    {
        var model = new ScriptedWritingModel("never");

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Write(model, new WriteRequest { Instruction = "  ", Document = "⟦cursor⟧" }, new RecordingStreamWriter<WriteReply>()));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Null(model.User);
    }

    [Fact]
    public async Task A_model_failure_reads_as_unavailable()
    {
        var replies = new RecordingStreamWriter<WriteReply>();

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Write(new ScriptedWritingModel("Half a ") { Fails = true }, new WriteRequest { Instruction = "Go on" }, replies));

        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(LoopErrors.WriterFailed, error.Status.Detail);
        Assert.Single(replies.Messages);
    }

    private static Task Write(IWritingModel model, WriteRequest request, IServerStreamWriter<WriteReply> replies) =>
        new WriterService(model, NullLogger<WriterService>.Instance).Write(request, replies, new TestServerCallContext());
}
