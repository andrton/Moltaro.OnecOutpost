using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Service;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// uninstall [--stop-timeout <TIME>]: elevation, the stop, the two rights taken from the service's account, and the
// service deleted. Both folders and the batch right of the worker accounts stay. The rights are removed before
// deleting the service so an interrupted run can be repeated. A run without the service ends with NotInstalled.
internal sealed class UninstallCommand : AsyncCommand<ServiceSettings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public UninstallCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ServiceSettings settings, CancellationToken cancellationToken)
    {
        string name = _parts.ServiceName;

        cancellationToken.ThrowIfCancellationRequested();
        SharedSteps.Elevation(_parts, "uninstall");

        cancellationToken.ThrowIfCancellationRequested();
        ServiceStopEnum stop = await SharedSteps.StopServiceAsync(_console, _parts, "uninstall", settings.StopTimeout, cancellationToken);

        if (stop == ServiceStopEnum.NotInstalled)
        {
            throw new CommandEndException(
                ConsoleExitCodes.NotInstalled,
                $"the service {name} is not installed; nothing was changed");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _parts.Rights.Revoke(_parts.AgentSid, AccountRightNames.AgentPrivileges);
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("rights", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _parts.Services.Delete(name);
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("service", exception);
        }

        _console.WriteLine(
            $"Removed the service {name} and its account's two rights. Left {_parts.ProgramFolder} and {_parts.DataFolder}, "
            + "with the audit log; the worker accounts keep \"Log on as a batch job\".");

        return ConsoleExitCodes.Success;
    }
}
