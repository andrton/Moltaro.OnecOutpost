using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Console.Workers;

// A worker account of check on Windows: the token of its logon, or of the process's own in a test, and the desktop that
// WorkerDesktop.Make gives the base for it on the console's own non-interactive window station, made at the first call
// of Desktop and kept, so that the probe and the workers of the base share it, as the workers of the service share
// theirs. It owns both and closes them when disposed. check calls it from one task at a time.
[SupportedOSPlatform("windows")]
internal sealed class WindowsWorkerAccount : IWorkerAccount
{
    private readonly string _baseKey;

    private WorkerDesktop? _desktop;

    public WindowsWorkerAccount(string baseKey, SafeAccessTokenHandle token)
    {
        _baseKey = baseKey;
        Token = token;
    }

    // The primary token, with which the Secondary Logon service creates the account's workers.
    public SafeAccessTokenHandle Token { get; }

    public bool IsOrdinary()
    {
        return WorkerLogon.IsOrdinary(Token);
    }

    public string Desktop()
    {
        _desktop ??= WorkerDesktop.Make(_baseKey, Token);

        return _desktop.Name;
    }

    public void Dispose()
    {
        _desktop?.Dispose();
        Token.Dispose();
    }
}
