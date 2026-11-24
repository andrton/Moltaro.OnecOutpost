using Moltaro.OnecOutpost.Agent.Policy;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The connection line of a COM worker, the first line of its input, in the form the COM worker reads: Location is
// Srvr="{server}";Ref="{database}"; or File="{file}";, each " in a value doubled; User the mode's Account; Password from
// AgentSecrets.Passwords; SafeMode the mode's; Executor the path of the executor. A class, not a record: it holds a
// password, and nothing prints it.
internal sealed class WorkerConnectionLine
{
    public required string Location { get; init; }

    public required string User { get; init; }

    public required string Password { get; init; }

    public required bool SafeMode { get; init; }

    public required string Executor { get; init; }

    // The line of the mode of the base, for the pool and for any run of a worker outside it: the base as the COM worker
    // completes it into a 1C connection string, the mode's Account and SafeMode, the password and the executor.
    public static WorkerConnectionLine For(AgentBasePolicy served, AgentModePolicy mode, string password, string executor)
    {
        return new WorkerConnectionLine
        {
            Location = LocationOf(served),
            User = mode.Account,
            Password = password,
            SafeMode = mode.SafeMode,
            Executor = executor,
        };
    }

    // Srvr and Ref for a cluster, File for a folder, each " in a value doubled.
    private static string LocationOf(AgentBasePolicy served)
    {
        if (served.File is string file)
        {
            return $"File=\"{Quoted(file)}\";";
        }

        return $"Srvr=\"{Quoted(served.Server!)}\";Ref=\"{Quoted(served.Database!)}\";";
    }

    private static string Quoted(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}
