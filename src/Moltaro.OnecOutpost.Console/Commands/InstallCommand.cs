using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Service;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// install [--stop-timeout <TIME>]: elevation and the source, the stop, the program folder judged by the agent's rule
// before any service exists, the service, the two rights of its account, and the data folder, each step idempotent and
// named in its failure, as the S10 brief says. It never starts the service, and it repairs neither folder: one that
// admits somebody else ends it.
internal sealed class InstallCommand : AsyncCommand<ServiceSettings>
{
    // The agent's program in the program folder, which the service runs with its one argument.
    private const string AgentProgram = "Moltaro.OnecOutpost.Agent.exe";

    private const string DisplayName = "Moltaro OnecOutpost agent";

    // ERROR_SERVICE_MARKED_FOR_DELETE: a handle that the Services console or Event Viewer holds keeps a deleted service.
    private const int MarkedForDeletion = 1072;

    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public InstallCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ServiceSettings settings, CancellationToken cancellationToken)
    {
        string sid = ServiceAccount.Sid(_parts.ServiceName);
        string account = $@"NT SERVICE\{_parts.ServiceName}";

        cancellationToken.ThrowIfCancellationRequested();
        string? source = Source();

        cancellationToken.ThrowIfCancellationRequested();
        ServiceStopEnum stop = await SharedSteps.StopServiceAsync(_console, _parts, "install", settings.StopTimeout, cancellationToken);

        if (stop == ServiceStopEnum.NotInstalled)
        {
            _console.WriteLine($"There is no service {_parts.ServiceName} yet.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ApplyProgramFolder(source, sid);

        cancellationToken.ThrowIfCancellationRequested();
        RegisterService(account);

        cancellationToken.ThrowIfCancellationRequested();
        GrantRights(sid, account);

        cancellationToken.ThrowIfCancellationRequested();
        ApplyDataFolder(sid);

        _console.WriteLine(
            "Installed; the service is not started. Start it once policy.json, the pairing and the passwords are written; an "
            + "update keeps those written before.");

        return ConsoleExitCodes.Success;
    }

    // Step 1: the elevation, then the folder that install copies the program from, null when it runs from the program
    // folder itself, so that the lists alone are set. A source inside the program folder would be emptied before it is
    // copied, and one that holds the program folder, such as an archive unpacked straight into %ProgramFiles%, would be
    // copied into it with all else it holds. The final paths make a short name, a subst drive or a junction name the
    // folder it leads to.
    private string? Source()
    {
        SharedSteps.Elevation(_parts, "install");

        string programFolder;
        string source;

        try
        {
            programFolder = _parts.Folders.FinalPath(_parts.ProgramFolder);
            source = _parts.Folders.FinalPath(_parts.Source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("source", exception);
        }

        if (string.Equals(source, programFolder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IsInside(source, programFolder) || IsInside(programFolder, source))
        {
            throw new CommandEndException(
                ConsoleExitCodes.Usage,
                $"unpack the archive outside {_parts.ProgramFolder} and run install from there");
        }

        return _parts.Source;
    }

    // Whether the path lies inside the folder, both final paths; a root, such as C:\, ends with its separator.
    private static bool IsInside(string path, string folder)
    {
        string prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // Step 3: the program folder, copied and given its lists, then judged by the agent's own rule before any service
    // exists that would run its code.
    private void ApplyProgramFolder(string? source, string sid)
    {
        string folder = _parts.ProgramFolder;
        IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers;

        try
        {
            _parts.Folders.ApplyProgramFolder(folder, source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("program folder", exception);
        }

        _console.WriteLine(source is null
            ? $"Set the access lists of {folder}."
            : $"Copied the program to {folder} and set its access lists.");

        try
        {
            writers = _parts.AccessLists(sid).Writers(folder);
        }
        catch (IOException exception)
        {
            throw CommandEndException.StepFailed("program folder", exception);
        }

        if (writers.Count == 0)
        {
            return;
        }

        Table table = new Table().AddColumns("Entry", "Who else may change it");

        foreach ((string path, IReadOnlyList<string> accounts) in writers)
        {
            table.AddRow(new Text(path), new Text(string.Join(", ", accounts)));
        }

        _console.Write(table);

        throw new CommandEndException(
            ConsoleExitCodes.ProgramFolderWriters,
            $"someone else may change {folder} through the entries above, so the agent would refuse to start; take those rights away and run install again");
    }

    // Step 4: the service under its virtual account, created, or reconfigured when it exists.
    private void RegisterService(string account)
    {
        string binaryPath = $"\"{Path.Combine(_parts.ProgramFolder, AgentProgram)}\" service";
        bool created;

        try
        {
            created = _parts.Services.Install(_parts.ServiceName, DisplayName, binaryPath);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == MarkedForDeletion)
        {
            throw new CommandEndException(
                ConsoleExitCodes.Failure,
                $"the step \"service\" failed: {CommandEndException.ReasonOf(exception)}; close the Services console and Event Viewer, and run install again");
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("service", exception);
        }

        _console.WriteLine(created
            ? $"Registered the service {_parts.ServiceName} under {account}."
            : $"Reconfigured the service {_parts.ServiceName} under {account}.");
    }

    // Step 5: the two privileges of the service's account, which it holds from its next start.
    private void GrantRights(string sid, string account)
    {
        try
        {
            _parts.Rights.Grant(sid, SharedSteps.AgentPrivileges);
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("rights", exception);
        }

        _console.WriteLine($"Granted {account} \"Replace a process level token\" and \"Adjust memory quotas for a process\".");
    }

    // Step 6: the data folder, created when it is absent; otherwise the folder, audit\ and outcomes\ are judged by the
    // agent's rule and given their lists again, since %ProgramData% lets every local user make a folder there first.
    private void ApplyDataFolder(string sid)
    {
        string folder = _parts.DataFolder;
        bool create;

        try
        {
            FolderKindEnum kind = _parts.Folders.Kind(folder);
            create = kind == FolderKindEnum.Absent;

            if (!create)
            {
                IAccessLists lists = _parts.AccessLists(sid);

                Judge(lists, "", kind);
                Judge(lists, "audit", _parts.Folders.Kind(Path.Combine(folder, "audit")));
                Judge(lists, "outcomes", _parts.Folders.Kind(Path.Combine(folder, "outcomes")));
            }

            _parts.Folders.ApplyDataFolder(folder, sid, create);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("data folder", exception);
        }

        _console.WriteLine(create ? $"Created {folder} with its access lists." : $"Set the access lists of {folder}.");
    }

    // An entry of the data folder, "" for the folder itself, that is a reparse point or admits anyone else.
    private void Judge(IAccessLists lists, string entry, FolderKindEnum kind)
    {
        if (kind == FolderKindEnum.Absent)
        {
            return;
        }

        string name = entry.Length == 0 ? _parts.DataFolder : $"{entry} of {_parts.DataFolder}";

        if (kind == FolderKindEnum.ReparsePoint)
        {
            throw Refused($"{name} is a reparse point");
        }

        IReadOnlyList<string> others = lists.Others(entry);

        if (others.Count > 0)
        {
            throw Refused($"{name} admits {string.Join(", ", others)}");
        }
    }

    private static CommandEndException Refused(string fault)
    {
        return new CommandEndException(
            ConsoleExitCodes.DataFolderAdmitsOthers,
            $"{fault}; install does not repair it: inspect it, remove it and run install again");
    }
}
