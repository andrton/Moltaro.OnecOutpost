using System.ComponentModel;
using Moltaro.OnecOutpost.Console.Workers;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// A worker account that FakeWorkerStarts logged on for a base, as the test scripted the account there: the judgment of
// its token, a line "token {base} {account}" of the operation list, gives whether it is an ordinary user's or fails
// with the Win32Exception of a token that cannot be read; its desktop, a line "desktop {base} {account}", gives the
// name of the desktop made for the base or fails with the Win32Exception the test gave. Disposing it closes both.
internal sealed class FakeWorkerAccount : IWorkerAccount
{
    private readonly FakeWorkerStarts _starts;

    public FakeWorkerAccount(FakeWorkerStarts starts, string baseKey, string account)
    {
        _starts = starts;
        Base = baseKey;
        Account = account;
    }

    public string Base { get; }

    public string Account { get; }

    public bool IsDisposed { get; private set; }

    // The name of the desktop that WorkerDesktop.Make gives the base on the console's own window station, as
    // lpDesktop names it.
    public static string DesktopOf(string baseKey)
    {
        return $@"Service-0x0-3e7a1$\OnecOutpost-{baseKey}-check";
    }

    public bool IsOrdinary()
    {
        _starts.Note($"token {Base} {Account}");

        if (_starts.TokenErrors.TryGetValue(Account, out int error))
        {
            throw new Win32Exception(error);
        }

        return !_starts.Administrators.Contains(Account);
    }

    public string Desktop()
    {
        _starts.Note($"desktop {Base} {Account}");

        if (_starts.DesktopFailures.TryGetValue(Account, out Win32Exception? failure))
        {
            throw failure;
        }

        return DesktopOf(Base);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
