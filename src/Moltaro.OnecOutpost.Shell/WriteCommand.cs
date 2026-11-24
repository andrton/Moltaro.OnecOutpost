using System.ComponentModel;
using Moltaro.OnecOutpost.Server;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class WriteCommand(ShellSession session) : AsyncCommand<WriteCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = ShellSession.Operand(settings.Base),
            CallerKey = ShellSession.Operand(settings.CallerKey),
            Script = session.Frame.Script,
            Parameters = session.Frame.Parameters,
        };
        OutpostOutcome outcome = await session.CallAsync("Waiting for the Write outcome", () =>
            session.Server.WriteAsync(request, cancellationToken));
        return session.Output.Outcome(outcome);
    }

    public sealed class Settings : BaseSettings
    {
        [CommandArgument(1, "<CALLER-KEY>")]
        [Description("The literal caller key that identifies this Write in the base.")]
        public string CallerKey { get; init; } = "";
    }
}
