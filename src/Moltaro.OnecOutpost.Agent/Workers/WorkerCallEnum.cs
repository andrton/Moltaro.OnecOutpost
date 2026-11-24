namespace Moltaro.OnecOutpost.Agent.Workers;

// How a worker's process is created.
internal enum WorkerCallEnum
{
    // CreateProcessAsUserW, the agent's: with the caller's own token, or a worker token that the agent's privileges let
    // it assign.
    AsUser,

    // CreateProcessWithTokenW through the Secondary Logon service, the console's: for a token not its own, with
    // "Impersonate a client after authentication" alone.
    WithToken,
}
