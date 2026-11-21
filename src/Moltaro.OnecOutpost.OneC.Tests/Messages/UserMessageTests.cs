using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Messages;

// The executor collects the user messages of one command, in their order, and clears them before the next (concept
// §12, O7). Before it checks the size of the outcome, it trims them to fit the outcome limit and marks that it did
// (concept rule 5).
public sealed class UserMessageTests
{
    private const string TwoHundredMessages = """
        Text = "";
        For Number = 1 To 100 Do
            Text = Text + "x";
        EndDo;
        For Number = 1 To 200 Do
            Message(Text);
        EndDo;
        Result = 1;
        """;

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task User_messages_arrive_in_their_order_with_their_own_command_only()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine first = await reader.RunAsync("Message(\"one\");\nMessage(\"two\");\nResult = 1;");
        OutcomeLine second = await reader.RunAsync("Result = 2;");

        first.AssertIs("succeeded", null);
        Assert.Equal(["one", "two"], first.Messages);
        Assert.False(first.MessagesTrimmed);

        second.AssertIs("succeeded", null);
        Assert.Empty(second.Messages);
    }

    // 200 messages of 100 characters are about 20 000 bytes, far beyond a limit of 4096.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task User_messages_beyond_the_outcome_limit_are_trimmed_to_fit_it()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(TwoHundredMessages, outcomeLimit: 4096);

        read.AssertIs("succeeded", null);
        Assert.True(read.MessagesTrimmed);
        Assert.NotEmpty(read.Messages);
        Assert.InRange(read.OutcomeBytes.Length, 1, 4096);
    }
}
