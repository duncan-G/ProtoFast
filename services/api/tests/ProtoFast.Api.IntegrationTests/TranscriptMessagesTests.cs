using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using Xunit;
using JournaledMessage = ProtoFast.DocumentImport.Engine.Storage.TranscriptMessage;

namespace ProtoFast.Api.IntegrationTests;

public class TranscriptMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Reads_a_tool_result_turn()
    {
        var json = """{"message":{"role":"User","toolCalls":[],"toolResults":[{"callId":"c1","name":"execute_code","content":"{\"units\":3}","isError":true}]}}""";

        var message = TranscriptMessages.From(new JournaledMessage(4, json, At));

        Assert.Equal((4, TranscriptRole.User, ""), (message.Sequence, message.Role, message.Text));
        var result = Assert.Single(message.ToolResults);
        Assert.Equal(("c1", "execute_code", """{"units":3}""", true), (result.CallId, result.Name, result.Content, result.IsError));
        Assert.Null(message.Spend);
        Assert.Equal(At.ToUnixTimeMilliseconds(), message.RecordedUnixMs);
    }

    [Fact]
    public void An_entry_that_is_not_json_still_shows_as_text()
    {
        var message = TranscriptMessages.From(new JournaledMessage(0, "not json", At));

        Assert.Equal("not json", message.Text);
        Assert.Equal(TranscriptRole.Unspecified, message.Role);
    }
}
