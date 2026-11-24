using System.Net;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line ClockSkew of check (the S10b brief, "The server and the skew", and §2 of docs/protocol.md): the time of a
// verified answer, a confirmation or a refusal, against the middle of the round trip on this machine's clock,
// skew = time − (t0 + (t1 − t0) / 2), known within uncertainty = (t1 − t0) / 2 + 1 ms, the millisecond of §2. It passes
// when |skew| + uncertainty ≤ allowed, is beyond the allowed skew when |skew| − uncertainty > allowed, and cannot be
// judged otherwise; the allowed skew is --clock-skew, two minutes when absent. Without a verified answer it is not
// checked. The stub moves the clock of the machine by the round trip before it answers with a time of the server's.
public sealed class ClockSkewLineTests : IDisposable
{
    private const string Remedy =
        "; with the server ahead, the agent refuses every command with clock-skew, and with it behind, with expired once the gap also passes the take timeout: set the clocks";

    private static readonly TimeSpan RoundTrip = TimeSpan.FromMilliseconds(200);

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // A round trip of 200 ms gives an uncertainty of 101 ms; the bounds are exact.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(5_000, "the server's clock is ahead of this machine's by 5000 ms ± 101 ms, within 00:02:00")]
    [InlineData(-5_000, "the server's clock is behind this machine's by 5000 ms ± 101 ms, within 00:02:00")]
    [InlineData(119_899, "the server's clock is ahead of this machine's by 119899 ms ± 101 ms, within 00:02:00")]
    public async Task A_skew_known_to_be_within_the_allowed_skew_passes_saying_which_clock_is_ahead(int skew, string reason)
    {
        CheckRun run = await RunAsync(RoundTrip, TimeSpan.FromMilliseconds(skew));

        Assert.Equal(reason, run.Passed(CheckLineEnum.ClockSkew));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(180_000, "the server's clock is ahead of this machine's by 180000 ms ± 101 ms, beyond 00:02:00")]
    [InlineData(-180_000, "the server's clock is behind this machine's by 180000 ms ± 101 ms, beyond 00:02:00")]
    [InlineData(120_102, "the server's clock is ahead of this machine's by 120102 ms ± 101 ms, beyond 00:02:00")]
    public async Task A_skew_known_to_be_beyond_the_allowed_skew_fails_saying_what_the_agent_then_refuses(int skew, string reason)
    {
        CheckRun run = await RunAsync(RoundTrip, TimeSpan.FromMilliseconds(skew));

        run.AssertCheckFailed();
        Assert.Equal(reason + Remedy, run.Failed(CheckLineEnum.ClockSkew));
    }

    // Within the uncertainty of the round trip on either side of the allowed skew: 120101 ms ± 101 ms, and 118000 ms ±
    // 5001 ms after a round trip of ten seconds.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(200, 120_101, "cannot be judged within 00:02:00: the round trip took 200 ms")]
    [InlineData(10_000, 118_000, "cannot be judged within 00:02:00: the round trip took 10000 ms")]
    public async Task A_skew_that_the_round_trip_leaves_uncertain_fails_as_not_judged_naming_the_round_trip(int roundTrip, int skew, string reason)
    {
        CheckRun run = await RunAsync(TimeSpan.FromMilliseconds(roundTrip), TimeSpan.FromMilliseconds(skew));

        run.AssertCheckFailed();
        Assert.Equal(reason, run.Failed(CheckLineEnum.ClockSkew));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_allowed_skew_is_that_of_the_clock_skew_option()
    {
        CheckRun run = await RunAsync(RoundTrip, TimeSpan.FromSeconds(6), "--clock-skew", "00:00:05");

        run.AssertCheckFailed();
        Assert.Equal(
            "the server's clock is ahead of this machine's by 6000 ms ± 101 ms, beyond 00:00:05" + Remedy,
            run.Failed(CheckLineEnum.ClockSkew));
    }

    // A server that refuses the agent's signature still signs its refusal with its own key, and with its time.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_time_of_a_verified_refusal_is_judged_as_that_of_a_confirmation()
    {
        CheckRun run = await _checked.RunAsync(exchange =>
        {
            DateTimeOffset sent = Machine.Clock.GetUtcNow();
            Machine.Clock.Advance(RoundTrip);
            exchange.AnswerSigned(
                401,
                OutpostCodec.Write(new OutpostRefusal(sent + (RoundTrip / 2) + TimeSpan.FromSeconds(5), OutpostProtocolCodes.Signature)));
        });

        Assert.False(run.Row(CheckLineEnum.Server).Passed);
        Assert.Equal(
            "the server's clock is ahead of this machine's by 5000 ms ± 101 ms, within 00:02:00",
            run.Passed(CheckLineEnum.ClockSkew));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Without_a_verified_answer_the_skew_is_not_checked()
    {
        CheckRun run = await _checked.RunAsync(exchange => exchange.AnswerBare((int)HttpStatusCode.Forbidden));

        run.AssertCheckFailed();
        Assert.Equal("not checked: no verified answer came", run.Failed(CheckLineEnum.ClockSkew));
    }

    // The stub takes the outcome when check sent it, lets the round trip pass on the machine's clock, and confirms it
    // with the time of the middle of the round trip and the skew.
    private Task<CheckRun> RunAsync(TimeSpan roundTrip, TimeSpan skew, params string[] options)
    {
        return _checked.RunAsync(
            (ServerExchange exchange) =>
            {
                DateTimeOffset sent = Machine.Clock.GetUtcNow();
                Machine.Clock.Advance(roundTrip);
                exchange.AnswerSigned(
                    200,
                    OutpostCodec.Write(new OutpostOutcomeAnswer(sent + (roundTrip / 2) + skew, exchange.ReadOutcome().Command)));
            },
            options);
    }
}
