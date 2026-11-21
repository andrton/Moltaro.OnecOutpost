using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker;

// The first line of the worker's input, written once by the agent: the base, the account of the worker's mode, the safe
// mode of its executors and the path of the executor. A class, not a record, so that nothing prints the password by
// accident.
internal sealed class WorkerConnection
{
    // The part of a 1C connection string that names the base, for a cluster or for a file base, ended with its ";".
    public required string Location { get; init; }

    public required string User { get; init; }

    public required string Password { get; init; }

    public required bool SafeMode { get; init; }

    // The path of OutpostExecutor.epf.
    public required string Executor { get; init; }

    // Throws FormatException, with a reason that quotes nothing of the line, when the line cannot be read.
    public static WorkerConnection Read(byte[] line)
    {
        using JsonDocument document = LineJson.Parse(line);
        JsonElement[] members = LineJson.Members(
            document.RootElement,
            "location",
            "user",
            "password",
            "safeMode",
            "executor");

        return new WorkerConnection
        {
            Location = LineJson.ReadString(members[0], "location"),
            User = LineJson.ReadString(members[1], "user"),
            Password = LineJson.ReadString(members[2], "password"),
            SafeMode = LineJson.ReadBoolean(members[3], "safeMode"),
            Executor = LineJson.ReadString(members[4], "executor"),
        };
    }
}
