using System.Text;
using Moltaro.OnecOutpost.Server;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class ShellSession(ShellOutput output, IOutpostServer server)
{
    private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
    private readonly SessionInput _input = new();
    private CommandFrame? _frame;

    public ShellOutput Output => output;
    public IOutpostServer Server => server;
    public CommandFrame Frame => _frame!;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        int firstStatus = ShellExit.Success;
        try
        {
            while (!_input.EndOfInput)
            {
                output.Prompt();
                string? line = await _input.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int status;
                try
                {
                    status = await RunLineAsync(Header(line), cancellationToken);
                }
                catch (ShellError error)
                {
                    status = output.Error(error.Status, error.Message);
                }

                if (firstStatus == ShellExit.Success)
                {
                    firstStatus = status;
                }
            }
        }
        catch (DecoderFallbackException)
        {
            return output.Error(ShellExit.Usage, "Input must be valid UTF-8.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return firstStatus;
    }

    public Task<T> CallAsync<T>(string activity, Func<Task<T>> call)
    {
        return output.WaitAsync(activity, () =>
        {
            try
            {
                return call();
            }
            catch (ArgumentException exception)
            {
                // The server throws synchronously for caller mistakes. A fault of the returned task is a failure.
                throw new ShellError(ShellExit.Usage, exception.Message);
            }
        });
    }

    public static string Operand(string value)
    {
        return value[1..];
    }

    private CommandApp CreateCli(CancellationToken cancellationToken)
    {
        // Spectre disposes its registrations after each execution. The server and the session outlive that execution.
        CommandApp cli = new();
        cli.Configure(config =>
        {
            output.ConfigureCli(config, cancellationToken);
            config.Settings.Registrar.RegisterInstance(this);
            config.SetApplicationName("outpost");
            Add<TokenCommand>(config, "token", "Creates a pairing token for an agent.");
            Add<AgentsCommand>(config, "agents", "Lists agents and the bases they declare.");
            Add<RevokeCommand>(config, "revoke", "Revokes an agent and frees the base keys it held.");
            Add<ReleaseCommand>(config, "release", "Releases a key its holder no longer declares.");
            Add<ReadCommand>(config, "read", "Runs a Read. Enter the script, optional .parameters and a dot on its own line.");
            Add<WriteCommand>(config, "write", "Runs a Write. Enter the script, optional .parameters and a dot on its own line.");
        });
        return cli;
    }

    private void Add<T>(IConfigurator config, string name, string description) where T : class, ICommand
    {
        _commands.Add(name);
        config.AddCommand<T>(name).WithDescription(description);
    }

    private async Task<int> RunLineAsync(string[] words, CancellationToken cancellationToken)
    {
        CommandApp cli = CreateCli(cancellationToken);
        string command = words[0];
        if (command == "--help")
        {
            if (words.Length == 1)
            {
                return await cli.RunAsync(words, cancellationToken);
            }

            if (words.Length == 2 && _commands.Contains(words[1]))
            {
                return await cli.RunAsync([words[1], "--help"], cancellationToken);
            }

            throw new ShellError(ShellExit.Usage, "Use --help or --help followed by the name of one command.");
        }

        if (!_commands.Contains(command))
        {
            throw new ShellError(ShellExit.Usage, "Unknown command. Use --help to see the commands.");
        }

        if (command is "read" or "write")
        {
            // Even a bad header owns its frame. No line in it becomes a new command unless the frame ended.
            _frame = await CommandFrame.ReadAsync(_input, cancellationToken);
        }

        for (int index = 1; index < words.Length; index++)
        {
            // Spectre treats leading hyphens as options even for a positional caller key. Prefix every operand so
            // Spectre checks its position, then remove exactly the prefix in the command.
            words[index] = "=" + words[index];
        }

        int status = await cli.RunAsync(words, cancellationToken);
        _frame = null;
        return status;
    }

    private static string[] Header(string line)
    {
        ReadOnlySpan<char> header = line.AsSpan().TrimStart();
        int end = 0;
        while (end < header.Length && !char.IsWhiteSpace(header[end]))
        {
            end++;
        }

        string command = header[..end].ToString();
        ReadOnlySpan<char> rest = header[end..].TrimStart();
        if (rest.IsEmpty)
        {
            return [command];
        }

        return command == "token"
            ? [command, rest.ToString()]
            : [command, .. rest.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
    }
}
