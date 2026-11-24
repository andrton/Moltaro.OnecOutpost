using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Service;
using Spectre.Console;

namespace Moltaro.OnecOutpost.Console.Commands;

// The steps that more than one command takes, each written once (the S10 brief, "install", "uninstall" and "The steps
// pair and secret share"). Each ends its command with a CommandEndException, as the brief says.
internal static class SharedSteps
{
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
    // else giving DataFolderAdmitsOthers with the start's reason, since DPAPI for the machine lets every local process
    // decrypt and only the folder's list keeps the secrets; and policy.json read as the agent reads it, absent,
    // unreadable or invalid giving PolicyInvalid with the agent's reason. Limitation: an entry that only the folder's
    // content inherits, which only an administrator can add, reaches a file written there, which the agent's start then
    // refuses.
    public static AgentPolicy OpenDataFolder(ConsoleParts parts, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Elevation(parts, command);

        cancellationToken.ThrowIfCancellationRequested();
        JudgeDataFolder(parts);

        cancellationToken.ThrowIfCancellationRequested();

        return ReadPolicy(parts.Data.Root);
    }

    // Writes a file of the data folder by Replace, then makes Administrators its owner (Q14), since the agent counts
    // any other owner as somebody else. An IOException or an UnauthorizedAccessException passes.
    public static void WriteDataFile(ConsoleParts parts, string name, byte[] bytes)
    {
        parts.Data.Root.Replace(name, bytes);
        parts.Folders.OwnByAdministrators(Path.Combine(parts.DataFolder, name));
    }

    // The kind of the data folder, which pair, secret and check need: Absent ends the command with NotInstalled, and an
    // error of Windows or of the disk with Failure.
    public static FolderKindEnum DataFolderKind(ConsoleParts parts)
    {
        FolderKindEnum kind;

        try
        {
            kind = parts.Folders.Kind(parts.DataFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("data folder", exception);
        }

        if (kind == FolderKindEnum.Absent)
        {
            throw new CommandEndException(
                ConsoleExitCodes.NotInstalled,
                $"the data folder {parts.DataFolder} does not exist; run install first");
        }

        return kind;
    }

    // Whether the path lies inside the folder, both final paths, which install and metadata judge; a root, such as C:\,
    // ends with its separator.
    public static bool IsInside(string path, string folder)
    {
        string prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // The reason of a data folder that is a reparse point, for check and for the commands that run after install, whose
    // remedy removes nothing, since an installed data folder holds the audit log and the outcomes not yet sent.
    public static string ReparsePointReason(string folder)
    {
        return $"{folder} is a reparse point; inspect it and correct it, never remove what it holds";
    }

    // The data folder of pair and secret, which run after install: a reparse point with check's reason, and one that
    // admits anyone else with the start's, word for word, whose remedy removes no audit log either.
    private static void JudgeDataFolder(ConsoleParts parts)
    {
        FolderKindEnum kind = DataFolderKind(parts);

        if (kind == FolderKindEnum.ReparsePoint)
        {
            throw new CommandEndException(ConsoleExitCodes.DataFolderAdmitsOthers, ReparsePointReason(parts.DataFolder));
        }

        OtherAccounts others;

        try
        {
            others = parts.AccessLists(parts.AgentSid).Others("");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("data folder", exception);
        }

        if (StartChecks.AccessReason("", others) is string reason)
        {
            throw new CommandEndException(ConsoleExitCodes.DataFolderAdmitsOthers, reason);
        }
    }

    // As the agent's start reads it, by StartChecks, with the same reasons.
    private static AgentPolicy ReadPolicy(IAgentFolder data)
    {
        IReadOnlyList<string> names;

        try
        {
            names = data.Names();
        }
        catch (IOException exception)
        {
            throw CommandEndException.StepFailed("data folder", exception);
        }

        try
        {
            return StartChecks.ReadPolicy(data, names);
        }
        catch (AgentPolicyException exception)
        {
            throw new CommandEndException(ConsoleExitCodes.PolicyInvalid, StartChecks.PolicyReason(exception));
        }
    }
}
