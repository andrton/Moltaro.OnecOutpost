using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Connector;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// connector register: elevation, then one line for each finding of the finder, 64 bits first. A bitness without a
// registered connector and with one copy installed gets that copy registered by the regsvr32 of its bitness, and the
// finder, which reads HKLM as the service sees it, says whether it took; with several copies none is registered, since
// the connector has to match the version of the 1C cluster, and each is named with the command that registers it. The
// exit is RegistrationFailed when a run of regsvr32 failed, else ConnectorNotRegistered when no bitness ends with a
// registered connector, else Success, as the S10 brief says.
internal sealed class ConnectorRegisterCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public ConnectorRegisterCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedSteps.Elevation(_parts, "connector register");

        List<string> failed = [];
        bool registered = false;

        foreach (ConnectorFinding finding in _parts.Connectors.Find())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bits = finding.Bitness == ConnectorBitnessEnum.X64 ? "64-bit" : "32-bit";

            if (finding.Registered is ConnectorFile file)
            {
                _console.WriteLine($"{bits}: {file.Path}, {file.Version}, is registered.");
                registered = true;
            }
            else if (finding.Unregistered is [ConnectorFile copy])
            {
                if (await RegisterAsync(finding.Bitness, bits, copy, cancellationToken))
                {
                    registered = true;
                }
                else
                {
                    failed.Add(bits);
                }
            }
            else if (finding.Unregistered.Count > 1)
            {
                _console.WriteLine(
                    $"{bits}: none is registered, and the connector has to match the version of the 1C cluster, so register that one:");

                foreach (ConnectorFile each in finding.Unregistered)
                {
                    _console.WriteLine($"{each.Version}: {_parts.Registration.CommandOf(finding.Bitness, each.Path)}");
                }
            }
            else
            {
                _console.WriteLine($"{bits}: none is registered or installed.");
            }
        }

        if (failed.Count > 0)
        {
            throw new CommandEndException(
                ConsoleExitCodes.RegistrationFailed,
                $"regsvr32 registered no {string.Join(" or ", failed)} connector, as the line of each says; run connector register again once the cause is removed");
        }

        if (!registered)
        {
            throw new CommandEndException(
                ConsoleExitCodes.ConnectorNotRegistered,
                "no bitness has a COM connector registered, so the agent can start no COM worker; register the copy that matches the version of the 1C cluster");
        }

        return ConsoleExitCodes.Success;
    }

    // What an exit code of regsvr32 /s says.
    private static string KindOf(int code)
    {
        return code switch
        {
            1 => "bad arguments",
            2 => "OleInitialize failed",
            3 => "loading the DLL failed",
            4 => "no DllRegisterServer in the DLL",
            5 => "DllRegisterServer failed",
            _ => "a code that regsvr32 does not document",
        };
    }

    // Runs regsvr32 for the copy and reads HKLM again, since its exit code alone is never taken as the outcome. True
    // when the copy is then registered for its bitness.
    private async Task<bool> RegisterAsync(ConnectorBitnessEnum bitness, string bits, ConnectorFile copy, CancellationToken cancellationToken)
    {
        int code;

        try
        {
            code = await _console.Status().StartAsync(
                Markup.Escape($"Registering {copy.Path}…"),
                _ => _parts.Registration.RegisterAsync(bitness, copy.Path, cancellationToken));
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("regsvr32", exception);
        }

        if (code != 0)
        {
            _console.WriteLine($"{bits}: regsvr32 ended with {code}, {KindOf(code)}, for {copy.Path}.");

            return false;
        }

        if (_parts.Connectors.Find().Single(finding => finding.Bitness == bitness).Registered is not ConnectorFile now)
        {
            _console.WriteLine($"{bits}: regsvr32 ended with 0, yet HKLM shows no registration of {copy.Path}.");

            return false;
        }

        _console.WriteLine($"{bits}: registered {now.Path}, {now.Version}.");

        return true;
    }
}
