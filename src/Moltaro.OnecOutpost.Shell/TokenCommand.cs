using System.ComponentModel;
using Moltaro.OnecOutpost.Server;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class TokenCommand(ShellSession session) : AsyncCommand<TokenCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string name = ShellSession.Operand(settings.Name);
        OutpostPairingToken token = await session.CallAsync("Creating a pairing token", () =>
            session.Server.CreatePairingTokenAsync(name, cancellationToken));
        session.Output.Line($"Agent ID {token.AgentId:D}");
        session.Output.Line($"Expires at {token.ExpiresAt:O}");
        session.Output.Line($"Token {token.Token}");
        return ShellExit.Success;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("The agent's name. It can contain spaces.")]
        public string Name { get; init; } = "";
    }
}
