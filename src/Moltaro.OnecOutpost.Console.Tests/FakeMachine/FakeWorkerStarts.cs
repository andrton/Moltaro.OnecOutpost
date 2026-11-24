using System.ComponentModel;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The COM workers that check starts on a test's machine (IWorkerStarts), as the test scripts them. Each call is first a
// line of the operation list of the machine's data folder, which never holds a password:
// - "logon {base} {account}": the batch logon of a worker account, which fails with the Win32Exception of the code of
//   LogonUserW that the test gives the account, or gives a FakeWorkerAccount, its token an ordinary user's until the
//   test names the account among the administrators;
// - "probe {base} {account} {program} within {deadline}": the probe, which gives the exit code that the test gives the
//   account, 0 until it says otherwise, null for a worker ended at the deadline, or fails with the Win32Exception the
//   test gives the account;
// - "start {base} {program} under {account}", or "under the console" for the console's own token: a worker in read
//   mode, which fails with the Win32Exception that the test gives the base, or gives a FakeCheckProcess, whose session
//   the test scripts for the base and the user of its connection line.
// It keeps the passwords of the logons, the accounts and the workers it gave, for the test to read. Not thread-safe:
// check makes one call at a time, and only a kill of a worker may come from a timer.
internal sealed class FakeWorkerStarts : IWorkerStarts
{
    private readonly MemoryDataFolder _notes;

    private readonly List<(string Base, string Account, string Password)> _logOns = [];

    private readonly List<FakeWorkerAccount> _accounts = [];

    private readonly List<FakeCheckProcess> _processes = [];

    public FakeWorkerStarts(MemoryDataFolder notes)
    {
        _notes = notes;
    }

    // The code of LogonUserW for each worker account that cannot log on, such as 1326.
    public Dictionary<string, int> LogOnErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The worker accounts whose token is an administrator's, or holds a privilege beyond an ordinary user's.
    public HashSet<string> Administrators { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The Win32 error with which the token of each such worker account cannot be read.
    public Dictionary<string, int> TokenErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The failure of the desktop of each such worker account.
    public Dictionary<string, Win32Exception> DesktopFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The exit code of the probe under each worker account, null for one ended at its deadline; 0 for the others.
    public Dictionary<string, uint?> ProbeExits { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The failure of the call of the probe under each such worker account.
    public Dictionary<string, Win32Exception> ProbeFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The failure of the start of each worker of each such base.
    public Dictionary<string, Win32Exception> StartFailures { get; } = new(StringComparer.Ordinal);

    // The session of each 1C user of each base, by the base and the user of the connection line.
    public Dictionary<(string Base, string User), FakeSession> Sessions { get; } = [];

    // Each logon in its order, with the password given.
    public IReadOnlyList<(string Base, string Account, string Password)> LogOns => _logOns;

    public IReadOnlyList<FakeWorkerAccount> Accounts => _accounts;

    public IReadOnlyList<FakeCheckProcess> Processes => _processes;

    public IWorkerAccount LogOn(string baseKey, string account, string password)
    {
        Note($"logon {baseKey} {account}");
        _logOns.Add((baseKey, account, password));

        if (LogOnErrors.TryGetValue(account, out int error))
        {
            throw new Win32Exception(error);
        }

        FakeWorkerAccount logged = new(this, baseKey, account);
        _accounts.Add(logged);

        return logged;
    }

    public Task<uint?> ProbeAsync(IWorkerAccount account, string program, string baseKey, TimeSpan deadline, CancellationToken cancellationToken)
    {
        string name = ((FakeWorkerAccount)account).Account;

        Note($"probe {baseKey} {name} {program} within {deadline}");

        if (ProbeFailures.TryGetValue(name, out Win32Exception? failure))
        {
            return Task.FromException<uint?>(failure);
        }

        return Task.FromResult(ProbeExits.TryGetValue(name, out uint? exit) ? exit : 0u);
    }

    public CheckWorker Start(IWorkerAccount? account, string program, string baseKey)
    {
        string? name = (account as FakeWorkerAccount)?.Account;

        Note($"start {baseKey} {program} under {name ?? "the console"}");

        if (StartFailures.TryGetValue(baseKey, out Win32Exception? failure))
        {
            throw failure;
        }

        FakeCheckProcess process = new(this, baseKey, program, name);
        _processes.Add(process);

        return new CheckWorker(process, process.Diagnostics);
    }

    // The session that the test scripted for the user of the base; a user that it names not opens and gives its own
    // name with an empty full name.
    public FakeSession SessionOf(string baseKey, string user)
    {
        return Sessions.TryGetValue((baseKey, user), out FakeSession? session) ? session : new FakeSession(user, "");
    }

    public void Note(string line)
    {
        _notes.Note(line);
    }
}
