using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.Host;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// A lost answer to a poll (§6.2 and §9 of docs/protocol.md, the S6 brief, "AgentLoop.RunAsync"): one that CheckAnswer
// refuses, its reason status-200, included. The agent acts on nothing in it: it records connection lost when the state
// is not, and nothing else, so no command is recorded or run, no bases, no retention, and the numbers are not
// answered. Then it waits a growing delay: a tenth of retryDelayMax, doubled up to it, reset by an answer that is not
// lost.
public sealed class LostAnswerTests
{
    // The answer holds a command and bases, and the audit log a file that retention would remove, more than 90 days
    // plus a day old. The agent counts its polls, as its log holds no reservation, so the next poll is number 2 while
    // no answer is acted on.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("more-than-free")]
    [InlineData("two-of-one-id")]
    [InlineData("signature-that-does-not-verify")]
    public async Task Nothing_in_a_lost_answer_is_acted_on_and_its_commands_are_neither_recorded_nor_run(string answer)
    {
        PairedFolder folder = PairedFolder.Create();
        DateTimeOffset old = TestTimes.Start.AddDays(-100);
        folder.Data.Audit.Seed(FileOf(old), Started(old));
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        ServerExchange first = await host.NextPollAsync();
        int count = host.Data.Changes.Count;
        Give(host, first, answer);
        await host.PassExactlyAsync(TimeSpan.FromSeconds(3));
        OutpostPollRequest second = (await host.NextPollAsync()).ReadPoll();

        Assert.Equal<string>(["connection", "request poll"], Operations.Since(host.Data, count));
        JsonElement lost = AuditEntries.AppendedSince(host.Data, count)[0];
        Assert.Equal("lost", lost.GetProperty("state").GetString());
        Assert.Equal("status-200", lost.GetProperty("reason").GetString());
        Assert.Equal(2, second.Number);
        Assert.Empty(second.Received);
    }

    // retryDelayMax is 4 seconds: 0.4, 0.8, 1.6 and 3.2 seconds, then 4, and 4 again; an ordinary answer, after which
    // the next poll opens at once, and the delay begins again at 0.4.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task After_a_lost_answer_the_agent_waits_a_tenth_of_retryDelayMax_doubled_up_to_it_until_an_answer_is_not_lost()
    {
        PairedFolder folder = PairedFolder.Create();
        folder.Data.Root.Seed("policy.json", PairedFolder.PolicyWith("\"retryDelayMax\": \"00:00:04\""));
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        double[] delays = [0.4, 0.8, 1.6, 3.2, 4, 4];

        foreach (double seconds in delays)
        {
            (await host.NextPollAsync()).Fail(new HttpRequestException("The connection was reset."));
            await host.PassExactlyAsync(TimeSpan.FromSeconds(seconds));
        }

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        (await host.NextPollAsync()).Fail(new HttpRequestException("The connection was reset."));
        await host.PassExactlyAsync(TimeSpan.FromSeconds(0.4));

        await host.NextPollAsync();
    }

    private static void Give(TestHost host, ServerExchange exchange, string answer)
    {
        DateTimeOffset now = TestTimes.Start;
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, now);
        OutpostPollAnswer withBases = host.Server.Ordinary() with { Held = ["accounting"], Claimed = ["warehouse"] };

        switch (answer)
        {
            case "more-than-free":
                // Two Reads of accounting, which declared one free worker for Read, within the three of the poll.
                OutpostCommandMessage another = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, now);
                exchange.AnswerPoll(withBases with { Commands = [host.Server.Sign(read), host.Server.Sign(another)] });
                break;
            case "two-of-one-id":
                OutpostCommandMessage twin = read with { Base = "warehouse" };
                exchange.AnswerPoll(withBases with { Commands = [host.Server.Sign(read), host.Server.Sign(twin)] });
                break;
            case "signature-that-does-not-verify":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    byte[] body = OutpostCodec.Write(withBases with { Commands = [host.Server.Sign(read)] });
                    exchange.AnswerSignedBy(other, 200, body);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }
}
