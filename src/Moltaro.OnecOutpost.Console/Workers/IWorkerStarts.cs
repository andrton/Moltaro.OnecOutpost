using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Console.Workers;

// The COM workers that check and metadata start: the boundary that WindowsWorkerStarts implements and tests replace. A
// worker of the console opens no pool, and never runs a Write.
internal interface IWorkerStarts
{
    // One batch logon of the local account, by WorkerLogon.LogOn, for the base's desktop; the caller disposes it. A
    // failed logon is a Win32Exception with the code of LogonUserW, never with the password. One call per account per
    // run, never retried, since each failure counts toward a lockout.
    IWorkerAccount LogOn(string baseKey, string account, string password);

    // The probe: the program under the account on its desktop, through the Secondary Logon service, with the command
    // line "{program}" {base key} write and its input closed before it resumes, so that it opens no base, in a job of its
    // own; its exit code, or null once it was ended at the deadline. A failed call is a Win32Exception with its code.
    Task<uint?> ProbeAsync(IWorkerAccount account, string program, string baseKey, TimeSpan deadline, CancellationToken cancellationToken);

    // A worker of the program in read mode, "{program}" {base key} read, in a job of its own: with lpDesktop "" when
    // account is null, under a restricted version of the console's own token, without a privilege, with Administrators
    // for deny only and at medium integrity, so without the rights of an administrator and out of reach of the elevated
    // terminal, yet still the administrator's own account, unelevated, whose default owner and list are the user's, so
    // that the worker reopens what it creates; else under the account on its desktop.
    // Diagnostics is its standard error read to its end as UTF-8. A failed call is a Win32Exception with its code, a pipe
    // Windows cannot make an IOException.
    ConsoleWorker Start(IWorkerAccount? account, string program, string baseKey);
}
