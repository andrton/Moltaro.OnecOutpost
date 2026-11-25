using Moltaro.OnecOutpost.Server;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class ReadCommand(ShellSession session) : AsyncCommand<BaseSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        OutpostReadRequest request = new()
        {
            BaseKey = ShellSession.Operand(settings.Base),
            Script = session.Frame.Script,
            Parameters = session.Frame.Parameters,
        };
        OutpostOutcome outcome = await session.CallAsync("Waiting for the Read outcome", () =>
            session.Server.ReadAsync(request, cancellationToken));
        return session.Output.Outcome(outcome);
    }
}
