using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.Workers;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The lines that the tests give a worker of the test program, whose location and script name what it does, and the
// result of what it gave back.
internal static class TestLines
{
    // The outcome limit of a command line that a test gives, which bounds the line the worker may write back.
    public const long OutcomeLimit = 1024;

    // A connection line of the base accounting on its cluster, with a password that the line quotes, for the location
    // of the test program: any but no-session and line <text> answers {"ready":true}.
    public static WorkerConnectionLine Connection(string location = "Srvr=\"srv-1c:1541\";Ref=\"accounting\";")
    {
        return new WorkerConnectionLine
        {
            Location = location,
            User = "outpost-read",
            Password = "read \"pw\"",
            SafeMode = true,
            Executor = TestPool.Executor,
        };
    }

    // A command line whose script the test program runs.
    public static WorkerCommandLine Command(string script, long outcomeLimit = OutcomeLimit)
    {
        return new WorkerCommandLine(script, "{}", null, outcomeLimit);
    }

    // The result of the outcome the run gave, which the test program makes a succeeded one with its true size.
    public static JsonElement ResultOf((long Size, byte[] Outcome)? given)
    {
        Assert.NotNull(given);
        (long size, byte[] outcome) = given.Value;
        Assert.Equal(outcome.Length, size);

        using JsonDocument document = JsonDocument.Parse(outcome);

        return document.RootElement.GetProperty("result").Clone();
    }
}
