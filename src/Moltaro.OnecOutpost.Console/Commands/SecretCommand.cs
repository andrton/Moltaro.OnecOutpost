using System.ComponentModel;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// secret <BASE> <KIND>: the steps it shares with pair, then the password of the base's account in the mode, or of its
// worker account after "Log on as a batch job" is granted to it, written to secrets.dat with every other secret kept,
// as the S10 brief says. The password is asked with a prompt that does not echo it when the console is interactive,
// read as one line of the redirected input otherwise, and never written to the output.
internal sealed partial class SecretCommand : AsyncCommand<SecretCommand.Settings>
{
    private const string Worker = "worker";

    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public SecretCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        AgentPolicy policy = SharedSteps.OpenDataFolder(_parts, "secret", cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        AgentSecrets secrets = ReadSecrets();

        cancellationToken.ThrowIfCancellationRequested();
        string account = AccountOf(policy, settings);

        if (settings.Kind == Worker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GrantBatchLogon(account, settings.Base);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string whose = $"{account} for {ModeOf(settings.Kind)} of the base {settings.Base}";
        string password = await ReadPasswordAsync(whose, settings.Kind, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Write(Replaced(secrets, settings, password));

        _console.WriteLine($"Wrote the password of {whose}; the service uses it from its next start.");

        return ConsoleExitCodes.Success;
    }

    private static string ModeOf(string kind)
    {
        return kind switch
        {
            "read" => "the read mode",
            "write" => "the write mode",
            _ => "the workers",
        };
    }

    // The account of the kind that the policy names for the base.
    private static string AccountOf(AgentPolicy policy, Settings settings)
    {
        string key = settings.Base;

        if (policy.Bases.SingleOrDefault(basePolicy => basePolicy.Key == key) is not AgentBasePolicy basePolicy)
        {
            throw NotInPolicy($"the policy names no base {key}");
        }

        return settings.Kind switch
        {
            "read" => basePolicy.Read.Account,
            "write" => basePolicy.Write?.Account
                ?? throw NotInPolicy($"the base {key} is in the policy, which does not let it be written"),
            _ => basePolicy.Write?.WorkerAccount
                ?? throw NotInPolicy($"the base {key} is in the policy, which names no worker account for it"),
        };
    }

    private static CommandEndException NotInPolicy(string reason)
    {
        return new CommandEndException(ConsoleExitCodes.NotInPolicy, reason);
    }

    // The secrets with the password of the base and kind replaced, every other kept.
    private static AgentSecrets Replaced(AgentSecrets secrets, Settings settings, string password)
    {
        Dictionary<(string Base, OutpostModeEnum Mode), string> passwords = new(secrets.Passwords);
        Dictionary<string, string> workerPasswords = new(secrets.WorkerPasswords);

        switch (settings.Kind)
        {
            case "read":
                passwords[(settings.Base, OutpostModeEnum.Read)] = password;
                break;
            case "write":
                passwords[(settings.Base, OutpostModeEnum.Write)] = password;
                break;
            default:
                workerPasswords[settings.Base] = password;
                break;
        }

        return new AgentSecrets(secrets.PrivateKey, passwords, workerPasswords);
    }

    // secrets.dat as pair wrote it and secret since; one that is absent means that the agent is not paired.
    private AgentSecrets ReadSecrets()
    {
        byte[] file;

        try
        {
            file = _parts.Data.Root.ReadAll(AgentFolders.SecretsName);
        }
        catch (FileNotFoundException)
        {
            throw new CommandEndException(ConsoleExitCodes.NotPaired, $"{AgentFolders.SecretsName} is absent; pair the agent first");
        }
        catch (IOException exception)
        {
            throw new CommandEndException(
                ConsoleExitCodes.Failure,
                $"{AgentFolders.SecretsName} cannot be read: {CommandEndException.ReasonOf(exception)}");
        }

        try
        {
            return SecretsFile.Read(file, _parts.Protection);
        }
        catch (FormatException exception)
        {
            throw new CommandEndException(ConsoleExitCodes.Failure, $"{AgentFolders.SecretsName} cannot be read: {exception.Message}");
        }
        catch (CryptographicException)
        {
            // Such as a file copied from another machine: the agent's key in it is lost with it.
            throw new CommandEndException(
                ConsoleExitCodes.Failure,
                $"{AgentFolders.SecretsName} cannot be decrypted on this machine; to pair the agent anew, remove agent.json");
        }
    }

    // The worker account is a local user of this machine, named so to Windows, so that a domain account of the same
    // name is never meant; it gets the right first, so that the password is asked only of an account that can use it.
    private void GrantBatchLogon(string workerAccount, string key)
    {
        string account = $@"{Environment.MachineName}\{workerAccount}";
        string? sid;

        try
        {
            sid = _parts.Rights.UserSid(account);
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("worker account", exception);
        }

        if (sid is null)
        {
            throw new CommandEndException(
                ConsoleExitCodes.UnknownAccount,
                $"{workerAccount} of the base {key} is not a local user of this machine");
        }

        try
        {
            _parts.Rights.Grant(sid, [AccountRightNames.BatchLogon]);
        }
        catch (Win32Exception exception)
        {
            throw CommandEndException.StepFailed("rights", exception);
        }

        _console.WriteLine($"Granted {workerAccount} \"Log on as a batch job\".");
    }

    // The password from a prompt that echoes nothing, or one line of the redirected input. A read of a pipe does not
    // end on a cancel, so the wait for it is left instead. An empty password is one of a 1C account, a real case of 1C,
    // and refused for a worker account, whose blank password Windows refuses outside its console.
    private async Task<string> ReadPasswordAsync(string whose, string kind, CancellationToken cancellationToken)
    {
        string? password;

        if (_console.Profile.Capabilities.Interactive)
        {
            TextPrompt<string> prompt = new TextPrompt<string>(Markup.Escape($"Password of {whose}:"))
                .Secret(null)
                .AllowEmpty();

            password = await _console.PromptAsync(prompt, cancellationToken);
        }
        else if (_parts.Input is TextReader input)
        {
            password = await input.ReadLineAsync(cancellationToken).AsTask().WaitAsync(cancellationToken);
        }
        else
        {
            throw new CommandEndException(
                ConsoleExitCodes.NotInteractive,
                "run secret in a terminal, or give the password through standard input");
        }

        if (password is null)
        {
            throw new CommandEndException(ConsoleExitCodes.Usage, "the standard input ended before a line with the password");
        }

        if (password.Length == 0 && kind == Worker)
        {
            throw new CommandEndException(
                ConsoleExitCodes.Usage,
                "the password of a worker account cannot be empty, since Windows refuses a blank password outside its console");
        }

        return password;
    }

    private void Write(AgentSecrets secrets)
    {
        try
        {
            SharedSteps.WriteDataFile(_parts, AgentFolders.SecretsName, SecretsFile.Write(secrets, _parts.Protection));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw CommandEndException.StepFailed("secrets", exception);
        }
    }
}
