namespace Moltaro.OnecOutpost.ComWorker;

// One session in one base, under the account of the worker's mode. Worker holds it for its whole life, so what a script
// changes in the session can be seen by the next command of that worker (concept §3). Dispose ends the session and
// releases what it holds.
internal interface IOneCSession : IDisposable
{
    // Opens a fresh executor from the .epf at the path, in safe mode or without it. The session of ComConnector opens
    // it with an UnsafeOperationProtectionDescription whose warnings are off, since nobody can answer the platform's
    // warning in an external connection. It throws when the executor does not open.
    IOneCExecutor OpenExecutor(string path, bool safeMode);
}
