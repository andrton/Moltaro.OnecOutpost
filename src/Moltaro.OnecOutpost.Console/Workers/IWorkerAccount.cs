namespace Moltaro.OnecOutpost.Console.Workers;

// A worker account logged on by IWorkerStarts.LogOn, which owns its token and its desktop and closes them when
// disposed.
internal interface IWorkerAccount : IDisposable
{
    // WorkerAccountRule over the token's groups and privileges; a Win32Exception when they cannot be read.
    bool IsOrdinary();

    // The name of the account's desktop, made at the first call by WorkerDesktop.Make on the console's own
    // non-interactive window station; a Win32Exception when it cannot be made.
    string Desktop();
}
