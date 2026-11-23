using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

internal sealed partial class PairCommand
{
    // The arguments of pair: the server's address, absolute and allowed by §11 of docs/protocol.md as ServerRoute judges
    // it, and the pairing token of §5. An error names the rule and quotes neither, the token being a secret.
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SERVER>")]
        [Description("The address of the server, as the application gives it.")]
        public string Server { get; init; } = "";

        [CommandArgument(1, "<TOKEN>")]
        [Description("The pairing token that the application created for this agent.")]
        public string Token { get; init; } = "";

        public override ValidationResult Validate()
        {
            if (!Uri.TryCreate(Server, UriKind.Absolute, out Uri? server) || !ServerRoute.IsAllowed(server))
            {
                return ValidationResult.Error("<SERVER>: expected an https address, or an http one on loopback (§11 of the protocol)");
            }

            if (!OutpostPairingTokenText.TryRead(Token, out _))
            {
                return ValidationResult.Error("<TOKEN>: expected a pairing token as the application gave it");
            }

            return ValidationResult.Success();
        }
    }
}
