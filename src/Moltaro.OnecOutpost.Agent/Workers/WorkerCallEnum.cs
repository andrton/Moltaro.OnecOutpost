namespace Moltaro.OnecOutpost.Agent.Workers;

// How a worker's process is created.
internal enum WorkerCallEnum
{
    // CreateProcessAsUserW: the agent's, with its own token or a worker token that its privileges let it assign; and the
    // console's, with a restricted version of its own token, which needs no privilege.
    AsUser,

    // CreateProcessWithTokenW through the Secondary Logon service, the console's: for a token not its own, with
    // "Impersonate a client after authentication" alone.
    WithToken,
}
