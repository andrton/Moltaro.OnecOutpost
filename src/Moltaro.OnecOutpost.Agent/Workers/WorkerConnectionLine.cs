namespace Moltaro.OnecOutpost.Agent.Workers;

// The connection line of a COM worker, the first line of its input, in the form of S9's worker: Location is
// Srvr="{server}";Ref="{database}"; or File="{file}";, each " in a value doubled; User the mode's Account; Password from
// AgentSecrets.Passwords; SafeMode the mode's; Executor the pool's path of the executor. A class, not a record: it holds
// a password, and nothing prints it.
internal sealed class WorkerConnectionLine
{
    public required string Location { get; init; }

    public required string User { get; init; }

    public required string Password { get; init; }

    public required bool SafeMode { get; init; }

    public required string Executor { get; init; }
}
