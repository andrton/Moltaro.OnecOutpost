using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class RevokeCommand(ShellSession session) : AsyncCommand<RevokeCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        bool revoked = await session.CallAsync("Revoking the agent", () =>
            session.Server.RevokeAgentAsync(settings.Id, cancellationToken));
        session.Output.Line(revoked ? "Revoked" : "NotRevoked", revoked ? Color.Green : Color.Yellow);
        return ShellExit.Success;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The agent ID.")]
        public string AgentId { get; init; } = "";

        internal Guid Id { get; private set; }

        public override ValidationResult Validate()
        {
            if (!Guid.TryParse(ShellSession.Operand(AgentId), out Guid id))
            {
                return ValidationResult.Error("The agent ID must be a GUID.");
            }

            Id = id;
            return ValidationResult.Success();
        }
    }
}
