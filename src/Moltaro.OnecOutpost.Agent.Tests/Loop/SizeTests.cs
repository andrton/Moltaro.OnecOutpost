using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// An outcome refused for its size (concept rule 11, the row of result-too-large in the table of §9 of
// docs/protocol.md, the S6 brief, "TooLarge"): a verified result-too-large, or a bare 413 whatever its body, as a hop
// on the path sends one, replaces an outcome of kind succeeded or failed once: its outbox file and a second ended hold
// Unknown result-too-large for a Write and Failed result-too-large for a Read, with the duration of the first, and that
// goes next. For the replacement any answer but a verified 200 is lost, and an outcome of kind refused or unknown, or
// one with the code result-too-large already, is never replaced: it is sent again as it is.
public sealed class SizeTests
{
    [Theory]
    [InlineData("read", "failed", "refusal")]
    [InlineData("write", "unknown", "refusal")]
    [InlineData("read", "failed", "html-413")]
    [InlineData("write", "unknown", "html-413")]
    public async Task An_outcome_refused_for_its_size_is_replaced_once_recorded_with_a_second_ended_and_the_replacement_is_sent(
        string modeWord,
        string kindWord,
        string refusal)
    {
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        byte[] replacement = TestOutcomes.Own(TestOutcomes.KindOf(kindWord), OutpostOutcomeCodes.ResultTooLarge);
        PairedFolder folder = PairedFolder.Create();
        Guid command = EarlierRun.Ended(folder, mode, TestOutcomes.Succeeded());
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();

        ServerExchange first = await host.NextOutcomeAsync();
        int count = host.Data.Changes.Count;

        if (refusal == "refusal")
        {
            first.Refuse(413, OutpostProtocolCodes.ResultTooLarge);
        }
        else
        {
            first.AnswerHtml413();
        }

        ServerExchange second = await host.NextOutcomeAsync();

        Assert.Equal(command, second.ReadOutcome().Command);
        Assert.Equal(replacement, second.OutcomeBytes());
        Operations.AssertInOrder(Operations.Since(host.Data, count), $"replace {Operations.OutboxFile(command)}", $"ended {command}", "request outcome");
        Assert.Equal(replacement, host.Data.Changes.Skip(count).First(change => change.Line == $"replace {Operations.OutboxFile(command)}").Bytes);
        JsonElement ended = Assert.Single(AuditEntries.AppendedSince(host.Data, count), entry => AuditEntries.Kind(entry) == "ended");
        Assert.Equal(kindWord, ended.GetProperty("outcome").GetString());
        Assert.Equal(OutpostOutcomeCodes.ResultTooLarge, ended.GetProperty("code").GetString());
        Assert.Equal(EarlierRun.DurationMs, ended.GetProperty("durationMs").GetInt64());

        second.Confirm();
        await host.Data.WaitForAsync(change => change.Line == $"delete {Operations.OutboxFile(command)}");
    }

    // The replacement meets a bare 413 too: it is lost, and the same replacement is sent again after the delay.
    [Fact]
    public async Task For_the_replacement_any_answer_but_a_verified_200_is_lost_and_it_is_not_replaced_again()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid command = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.Succeeded());
        byte[] replacement = TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.ResultTooLarge);
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();

        (await host.NextOutcomeAsync()).AnswerBare(413);
        ServerExchange second = await host.NextOutcomeAsync();
        int count = host.Data.Changes.Count;
        second.AnswerBare(413);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        ServerExchange third = await host.NextOutcomeAsync();

        Assert.Equal(replacement, third.OutcomeBytes());
        Assert.Equal<string>(["request outcome"], Operations.Since(host.Data, count));
    }

    // A Refused outcome, an Unknown one, and a Failed result-too-large of the executor's own.
    [Theory]
    [InlineData("refused")]
    [InlineData("unknown")]
    [InlineData("failed-result-too-large")]
    public async Task An_outcome_of_kind_refused_or_unknown_or_with_the_code_result_too_large_is_never_replaced(string outcome)
    {
        PairedFolder folder = PairedFolder.Create();
        (Guid command, byte[] bytes) = outcome switch
        {
            "refused" => (EarlierRun.Refused(folder, OutpostOutcomeCodes.NotServed), TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, OutpostOutcomeCodes.NotServed)),
            "unknown" => (EarlierRun.Ended(folder, OutpostModeEnum.Write, TestOutcomes.TransactionEnded()), TestOutcomes.TransactionEnded()),
            _ => (EarlierRun.Ended(folder, OutpostModeEnum.Read, TooLargeOfTheExecutor()), TooLargeOfTheExecutor()),
        };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();

        ServerExchange first = await host.NextOutcomeAsync();
        int count = host.Data.Changes.Count;
        first.AnswerBare(413);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        ServerExchange second = await host.NextOutcomeAsync();

        Assert.Equal(command, second.ReadOutcome().Command);
        Assert.Equal(bytes, second.OutcomeBytes());
        Assert.Equal<string>(["request outcome"], Operations.Since(host.Data, count));
    }

    // The executor's own Failed result-too-large, in its form.
    private static byte[] TooLargeOfTheExecutor()
    {
        return """{ "kind": "failed", "code": "result-too-large", "result": null, "errorText": null, "messages": [ "Too much" ], "messagesTrimmed": true }"""u8.ToArray();
    }
}
