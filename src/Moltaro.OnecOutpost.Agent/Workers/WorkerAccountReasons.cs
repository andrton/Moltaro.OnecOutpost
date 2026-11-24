using System.ComponentModel;
using System.Globalization;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The reasons about a worker account and the agent's privileges that the start and the console's check share, each
// with a remedy that exists. Apart from the launcher, so that the table is tested on every platform.
internal static class WorkerAccountReasons
{
    // After "the worker account {account} of the base {key}".
    public const string NotOrdinary =
        "is an administrator or holds a privilege beyond an ordinary user's; take it out of Administrators and of every group that grants a privilege, or name another local user in the policy";

    // After the name of the agent's account.
    public const string AgentLacksPrivileges =
        "lacks \"Replace a process level token\" or \"Adjust memory quotas for a process\"; install grants them, and the service has them from its next start";

    // The sentence for a Win32 error of LogonUserW, after "cannot log on: ". A code the table does not name gives the
    // message of Windows, which holds the code's meaning alone, never a value given to the call, and the code.
    public static string CannotLogOn(string baseKey, int error)
    {
        string newPassword = $"; set a new one and write it with secret {baseKey} worker";

        return error switch
        {
            1326 => $"unknown account or wrong password; check workerAccount in the policy, or write the password again with secret {baseKey} worker",
            1385 => $"it lacks \"Log on as a batch job\", or a \"Deny log on as a batch job\" names it; secret {baseKey} worker grants the right",
            1909 => "the account is locked out",
            1331 => "the account is disabled",
            1793 => "the account has expired",
            1330 => "its password has expired" + newPassword,
            1907 => "its password must be changed" + newPassword,
            1327 => "an account restriction refuses it, such as a blank password outside the console",
            1328 => "its logon hours do not allow it now",
            1329 => "it may not log on at this computer",
            _ => $"{new Win32Exception(error).Message} (Windows error {error.ToString(CultureInfo.InvariantCulture)})",
        };
    }
}
