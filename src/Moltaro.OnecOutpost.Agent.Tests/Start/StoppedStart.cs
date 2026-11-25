using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Audit;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// A start that fails at a step after the opening of the log: it records started with
// the error and withoutSafeMode empty, disposes the log, which releases its lock, and throws AgentStopException with
// the same reason.
internal static class StoppedStart
{
    // Starts the agent on the folder, asserts that the start stopped so, and gives the entries that it appended.
    public static List<JsonElement> Of(PairedFolder folder, TimeProvider clock)
    {
        int count = folder.Data.Changes.Count;

        AgentStopException exception = Assert.Throws<AgentStopException>(() => folder.Start(clock));

        List<JsonElement> entries = AuditEntries.AppendedSince(folder.Data, count);
        JsonElement started = entries[0];

        Assert.Equal("started", AuditEntries.Kind(started));
        Assert.Equal(PairedFolder.Version, started.GetProperty("version").GetString());
        Assert.Empty(started.GetProperty("withoutSafeMode").EnumerateArray());
        Assert.Equal(exception.Message, started.GetProperty("error").GetString());

        AuditLog.Open(folder.Data.Audit, clock).Dispose();

        return entries;
    }

    // The error that the started entry of a stopped start records.
    public static string Error(List<JsonElement> entries)
    {
        return entries[0].GetProperty("error").GetString()!;
    }
}
