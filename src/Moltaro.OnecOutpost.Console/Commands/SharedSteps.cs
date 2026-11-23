using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;
using Spectre.Console;

namespace Moltaro.OnecOutpost.Console.Commands;

// The steps that more than one command takes, each written once (the S10 brief, "install", "uninstall" and "The steps
// pair and secret share"). Each ends its command with a CommandEndException, as the brief says.
internal static class SharedSteps
{
    // The two privileges of the agent's account, which install grants and uninstall revokes: "Replace a process level
    // token" and "Adjust memory quotas for a process", with which the agent starts a worker under a worker account.
    public static readonly string[] AgentPrivileges = [AccountRightNames.AssignPrimaryToken, AccountRightNames.IncreaseQuota];

    // The first step of every command: a console that is not elevated, UAC's filtered token included, changes nothing.
    public static void Elevation(ConsoleParts parts, string command)
    {
        if (!parts.Elevated)
        {
            throw new CommandEndException(
                ConsoleExitCodes.NotElevated,
                $"{command} needs a console run as administrator; nothing was changed");
        }
    }

    // The stop of install and uninstall, with a spinner: a service that does not stop within the timeout ends the
    // command with StopTimedOut, and an error of Windows with Failure. A service that stopped is said to be; one that
    // does not exist is the caller's to judge.
    public static async Task<ServiceStopEnum> StopServiceAsync(
        IAnsiConsole console,
        ConsoleParts parts,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string name = parts.ServiceName;
        ServiceStopEnum stop;

        try
        {
            stop = await console.Status().StartAsync(
                Markup.Escape($"Stopping the service {name}…"),
                _ => parts.Services.StopAsync(name, timeout, cancellationToken));
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("stop", exception);
        }

        if (stop == ServiceStopEnum.TimedOut)
        {
            throw new CommandEndException(
                ConsoleExitCodes.StopTimedOut,
                $"the service {name} did not stop within {timeout:c}; run {command} again once it has, or with a longer --stop-timeout");
        }

        if (stop == ServiceStopEnum.Stopped)
        {
            console.WriteLine($"The service {name} is stopped.");
        }

        return stop;
    }

    // The steps that pair and secret take first, each after a check of the token: the elevation; the data folder, absent
    // giving NotInstalled and a reparse point DataFolderAdmitsOthers; its access list for the agent's account, anyone
    // else giving DataFolderAdmitsOthers, since DPAPI for the machine lets every local process decrypt and only the
    // folder's list keeps the secrets; and policy.json read as the agent reads it, absent, unreadable or invalid giving
    // PolicyInvalid with the agent's reason. Limitation: an entry that only the folder's content inherits, which only an
    // administrator can add, reaches a file written there, which the agent's start then refuses.
    public static AgentPolicy OpenDataFolder(ConsoleParts parts, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Elevation(parts, command);

        cancellationToken.ThrowIfCancellationRequested();
        JudgeDataFolder(parts);

        cancellationToken.ThrowIfCancellationRequested();

        return ReadPolicy(parts.Data);
    }

    // Writes a file of the data folder by Replace, then makes Administrators its owner (Q14), since the agent counts
    // any other owner as somebody else. An IOException or an UnauthorizedAccessException passes.
    public static void WriteDataFile(ConsoleParts parts, string name, byte[] bytes)
    {
        parts.Data.Replace(name, bytes);
        parts.Folders.OwnByAdministrators(Path.Combine(parts.DataFolder, name));
    }

    private static void JudgeDataFolder(ConsoleParts parts)
    {
        string folder = parts.DataFolder;
        FolderKindEnum kind;
        IReadOnlyList<string> others = [];

        try
        {
            kind = parts.Folders.Kind(folder);

            if (kind == FolderKindEnum.Folder)
            {
                others = parts.AccessLists(ServiceAccount.Sid(parts.ServiceName)).Others("");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("data folder", exception);
        }

        if (kind == FolderKindEnum.Absent)
        {
            throw new CommandEndException(
                ConsoleExitCodes.NotInstalled,
                $"the data folder {folder} does not exist; run install first");
        }

        if (kind == FolderKindEnum.ReparsePoint)
        {
            throw new CommandEndException(
                ConsoleExitCodes.DataFolderAdmitsOthers,
                $"the data folder {folder} is a reparse point; inspect it, remove it and run install again");
        }

        if (others.Count > 0)
        {
            throw new CommandEndException(
                ConsoleExitCodes.DataFolderAdmitsOthers,
                $"the data folder {folder} admits {string.Join(", ", others)}, who could read the secrets; inspect it, remove it and run install again");
        }
    }

    // As the agent's start reads it, with the same reasons.
    private static AgentPolicy ReadPolicy(IAgentFolder data)
    {
        const string Invalid = $"{AgentStart.PolicyName} is not valid";

        byte[] bytes;

        try
        {
            bytes = data.ReadAll(AgentStart.PolicyName);
        }
        catch (FileNotFoundException)
        {
            throw new CommandEndException(
                ConsoleExitCodes.PolicyInvalid,
                $"{Invalid}: the file is absent; expected the local policy of this machine");
        }
        catch (IOException exception)
        {
            throw new CommandEndException(
                ConsoleExitCodes.PolicyInvalid,
                $"{Invalid}: the file cannot be read: {CommandEndException.ReasonOf(exception)}");
        }

        try
        {
            return AgentPolicyReader.ReadFile(bytes);
        }
        catch (AgentPolicyException exception)
        {
            throw new CommandEndException(ConsoleExitCodes.PolicyInvalid, $"{Invalid}: {exception.Message}");
        }
    }
}
