using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Moltaro.OnecOutpost.Server;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class ShellOutput(IAnsiConsole console)
{
    private static readonly JsonSerializerOptions ResultJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly bool _interactive = !Console.IsInputRedirected
        && !Console.IsOutputRedirected && console.Profile.Capabilities.Interactive;

    public void ConfigureCli(IConfigurator config, CancellationToken cancellationToken)
    {
        config.ConfigureConsole(console);
        config.UseStrictParsing();
        config.Settings.CaseSensitivity = CaseSensitivity.All;
        config.SetExceptionHandler((exception, _) => exception switch
        {
            ShellError error => Error(error.Status, error.Message),
            OperationCanceledException when cancellationToken.IsCancellationRequested => ShellExit.Cancelled,
            CommandParseException => Error(ShellExit.Usage, "Check the command and its operands. Use --help for syntax."),
            CommandRuntimeException => Error(ShellExit.Usage, exception.Message),
            _ => Error(ShellExit.Failure, "The shell could not complete the operation."),
        });
    }

    public void Line(string text, Color? color = null)
    {
        string literal = Printable(text);

        if (_interactive && color is Color foreground)
        {
            console.WriteLine(literal, new Style(foreground));
        }
        else
        {
            console.Profile.Out.Writer.WriteLine(literal);
        }
    }

    public void Prompt()
    {
        if (_interactive)
        {
            console.Write(new Text("outpost> "));
        }
    }

    public Task WaitAsync(string activity, Func<Task> work)
    {
        return _interactive ? console.Status().StartAsync(activity, _ => work()) : work();
    }

    public Task<T> WaitAsync<T>(string activity, Func<Task<T>> work)
    {
        return _interactive ? console.Status().StartAsync(activity, _ => work()) : work();
    }

    public int Error(int status, string reason)
    {
        string kind = status switch
        {
            ShellExit.Usage => "Usage",
            ShellExit.Cancelled => "Cancelled",
            _ => "Failure",
        };

        Line($"{kind} ({status}) {reason}", Color.Red);
        return status;
    }

    public void Agents(IReadOnlyList<OutpostAgent> agents)
    {
        if (agents.Count == 0)
        {
            Line("No agents.");
            return;
        }

        if (!_interactive)
        {
            foreach (OutpostAgent agent in agents)
            {
                Line($"{agent.Id:D} {agent.Name} {agent.State} connected={Flag(agent.Connected)}");

                foreach (OutpostBase @base in agent.Bases)
                {
                    Line($"  {@base.Key} readOpen={Flag(@base.ReadOpen)} writeOpen={Flag(@base.WriteOpen)} held={Flag(@base.Held)}");
                }
            }

            return;
        }

        Table table = new Table().AddColumns("Agent ID", "Name", "State", "Connected", "Base", "readOpen", "writeOpen", "held");

        foreach (OutpostAgent agent in agents)
        {
            Text id = new(agent.Id.ToString("D"));
            Text name = new(agent.Name);
            Text state = new(agent.State.ToString());
            Text connected = new(Flag(agent.Connected));

            if (agent.Bases.Count == 0)
            {
                table.AddRow(id, name, state, connected, Text.Empty, Text.Empty, Text.Empty, Text.Empty);
                continue;
            }

            foreach (OutpostBase @base in agent.Bases)
            {
                table.AddRow(id, name, state, connected, new Text(@base.Key), new Text(Flag(@base.ReadOpen)),
                    new Text(Flag(@base.WriteOpen)), new Text(Flag(@base.Held)));
            }
        }

        console.Write(table);
    }

    public int Outcome(OutpostOutcome outcome)
    {
        (int status, Color color, string reason) = outcome.Kind switch
        {
            OutpostOutcomeKindEnum.Succeeded => (ShellExit.Success, Color.Green, "The script completed."),
            OutpostOutcomeKindEnum.Failed => (ShellExit.Failed, Color.Red, "The command failed."),
            OutpostOutcomeKindEnum.Refused => (ShellExit.Refused, Color.Red, "A check refused the command."),
            OutpostOutcomeKindEnum.Unavailable => (ShellExit.Unavailable, Color.Yellow, "The agent for this base is disconnected."),
            OutpostOutcomeKindEnum.Expired => (ShellExit.Expired, Color.Yellow, "The command did not reach the agent in time."),
            OutpostOutcomeKindEnum.Unknown => (ShellExit.Unknown, Color.Yellow, "The effect of the Write is unknown."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        if (outcome.Code is string code)
        {
            reason = Reason(code) ?? reason;
            Line($"{outcome.Kind} ({status}) {code} {reason}", color);
        }
        else
        {
            Line($"{outcome.Kind} ({status}) {reason}", color);
        }

        if (outcome.CommandId is Guid id)
        {
            Line($"Command ID {id:D}");
        }

        if (outcome.Result is JsonElement result)
        {
            Line("Result");
            Line(JsonSerializer.Serialize(result, ResultJson));
        }

        foreach (string message in outcome.Messages)
        {
            Line($"Message {message}");
        }

        if (outcome.ErrorText is string error)
        {
            Line($"Error {error}", Color.Red);
        }

        if (outcome.MessagesTrimmed)
        {
            Line("Messages or error text were truncated.", Color.Yellow);
        }

        return status;
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    private static string? Reason(string code)
    {
        return code switch
        {
            OutpostCodes.ScriptError => "1C could not complete the script or its transaction.",
            OutpostCodes.ResultType => "The result could not be represented as a supported JSON value.",
            OutpostCodes.ResultTooLarge => "The outcome exceeded its size limit.",
            OutpostCodes.RunLimit => "The command exceeded its run limit.",
            OutpostCodes.Lost => "The agent or worker could not finish the Read.",
            OutpostCodes.CallerKeyConflict => "The caller key belongs to a different command for this base.",
            OutpostCodes.UnknownBase => "No agent holds this base key.",
            OutpostCodes.ModeClosed => "The agent does not open this mode for the base.",
            OutpostCodes.NotServed => "The local policy does not serve this base or mode.",
            OutpostCodes.NotAddressed => "The command names a different agent.",
            OutpostCodes.ProtocolVersion => "The agent does not support this protocol version.",
            OutpostCodes.Malformed => "The command did not follow the protocol.",
            OutpostCodes.Expired => "The take deadline had passed at the agent.",
            OutpostCodes.ClockSkew => "The server's command time was too far ahead of the agent's clock.",
            OutpostCodes.Revoked => "The agent was revoked.",
            OutpostCodes.Disconnected => "The agent stayed disconnected beyond the command's run limit.",
            OutpostCodes.AgentStopped => "The agent or worker stopped while the Write ran.",
            OutpostCodes.TransactionEnded => "1C could not confirm the transaction's outcome.",
            OutpostCodes.BaseUnreachable => "The worker could not open the base or the executor.",
            OutpostCodes.ConnectionLost => "The worker's session in the base ended while the command ran.",
            _ => null,
        };
    }

    // Terminal control characters must not act as formatting when a message or error came from 1C.
    private static string Printable(string text)
    {
        StringBuilder? escaped = null;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsControl(character) && character is not ('\n' or '\t')
                && !(character == '\r' && index + 1 < text.Length && text[index + 1] == '\n'))
            {
                escaped ??= new StringBuilder().Append(text.AsSpan(0, index));
                escaped.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped?.Append(character);
            }
        }

        return escaped?.ToString() ?? text;
    }
}
