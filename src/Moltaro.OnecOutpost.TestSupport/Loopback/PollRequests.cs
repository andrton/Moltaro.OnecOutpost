using Moltaro.OnecOutpost.Protocol.Messages;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// Polls as the tests of the store and of the endpoints declare them: each base with one free worker per mode it opens
// and a run limit of ten minutes, and no received commands. The free workers and the run limits are the subject of the
// commands, not of these tests; the keys a poll declares and whether it opens Write on each are.
internal static class PollRequests
{
    // The version of the agent program in every poll and pairing of the tests.
    public const string AgentVersion = "0.1.0";

    private static readonly OutpostPollMode Mode = new(1, TimeSpan.FromMinutes(10));

    // A base that the local policy opens for Read only: its write is null (§6.2 of docs/protocol.md).
    public static OutpostPollBase ReadOnly(string key) => new(key, Mode, null);

    // A base that the local policy opens for Read and Write.
    public static OutpostPollBase ReadAndWrite(string key) => new(key, Mode, Mode);

    public static OutpostPollRequest Create(
        long number,
        DateTimeOffset time,
        TimeSpan pollTimeout,
        IReadOnlyList<OutpostPollBase> bases)
    {
        return new OutpostPollRequest(number, time, AgentVersion, pollTimeout, bases, []);
    }
}
