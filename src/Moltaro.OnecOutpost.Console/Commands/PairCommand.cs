using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// pair <SERVER> <TOKEN>: the steps it shares with secret, then the pairing of §6.1 of docs/protocol.md, writing
// secrets.dat with the new key alone and then agent.json, as the S10 brief says. agent.json so exists only with its
// key. A pairing that ends without a verified 200 writes nothing, and its line never holds text of an unverified body.
internal sealed partial class PairCommand : AsyncCommand<PairCommand.Settings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public PairCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        AgentPolicy policy = SharedSteps.OpenDataFolder(_parts, "pair", cancellationToken);

        // Settings.Validate accepted both.
        Uri server = new(settings.Server, UriKind.Absolute);
        OutpostPairingTokenText.TryRead(settings.Token, out OutpostPairingTokenText? token);

        cancellationToken.ThrowIfCancellationRequested();

        if (_parts.Data.Names().Contains(AgentStart.PairingName))
        {
            throw new CommandEndException(
                ConsoleExitCodes.AlreadyPaired,
                "the agent is paired; to pair it anew, remove agent.json; what its outbox holds then reaches the server under "
                + "the new identity, which drops it as another agent's (§6.3); pairing anew writes secrets.dat with the new "
                + "key alone, so the passwords are written again with secret");
        }

        cancellationToken.ThrowIfCancellationRequested();
        AgentPairingResult result;

        using (HttpMessageHandler handler = _parts.Handlers(server, policy.Proxy))
        {
            result = await _console.Status().StartAsync(
                Markup.Escape($"Pairing with {server}…"),
                _ => AgentPairing.PairAsync(
                    server,
                    token!,
                    _parts.Version,
                    handler,
                    policy.RequestTimeout,
                    _parts.Clock,
                    cancellationToken));
        }

        if (result.Settings is not AgentPairingSettings paired || result.PrivateKey is not byte[] privateKey)
        {
            throw PairingFailed(result);
        }

        // No check of the token here: the server has paired the agent, and only these files keep the pairing.
        AgentSecrets secrets = new(
            privateKey,
            new Dictionary<(string Base, OutpostModeEnum Mode), string>(),
            new Dictionary<string, string>());

        Write(AgentStart.SecretsName, () => SecretsFile.Write(secrets, _parts.Protection));
        Write(AgentStart.PairingName, () => AgentPairingFile.Write(paired));

        _console.WriteLine($"Paired with {paired.Server} as {paired.Agent}; write the passwords with secret.");

        return ConsoleExitCodes.Success;
    }

    // The status when one came, and the console's own reason in the words of §6.1, never text of the body.
    private static CommandEndException PairingFailed(AgentPairingResult result)
    {
        string reason = result.Failure switch
        {
            AgentPairingFailureEnum.Unreachable =>
                "no answer came: the server, or the proxy of the policy, could not be reached, or did not answer within the requestTimeout of the policy",
            AgentPairingFailureEnum.Refused =>
                "a bare 403: the server answers so to any failure of pairing, and so can anything on the path",
            AgentPairingFailureEnum.ProtocolVersion =>
                "an unsigned answer in the form the server uses for a protocol version it does not speak",
            AgentPairingFailureEnum.ServerKey => "the server's key does not match the fingerprint in the token",
            AgentPairingFailureEnum.Signature => "the answer's signature does not verify",

            // Answer: anything else.
            _ => "an answer that is not the signed answer of §6.1 of the protocol",
        };

        string status = result.Status is int code ? $"HTTP {code}: " : "";

        return new CommandEndException(ConsoleExitCodes.PairingFailed, status + reason);
    }

    // A file of the pairing, its content made and written; a failure leaves the agent paired on the server alone.
    private void Write(string name, Func<byte[]> content)
    {
        try
        {
            SharedSteps.WriteDataFile(_parts, name, content());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new CommandEndException(
                ConsoleExitCodes.Failure,
                $"the server paired the agent, but {name} could not be written: {CommandEndException.ReasonOf(exception)}; the application revokes it and gives a new token");
        }
    }
}
