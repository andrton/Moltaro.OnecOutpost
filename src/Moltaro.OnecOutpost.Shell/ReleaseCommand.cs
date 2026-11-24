using Moltaro.OnecOutpost.Server;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class ReleaseCommand(ShellSession session) : AsyncCommand<BaseSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        string key = ShellSession.Operand(settings.Base);
        OutpostReleaseResultEnum released = await session.CallAsync("Releasing the base key", () =>
            session.Server.ReleaseBaseAsync(key, cancellationToken));
        session.Output.Line(released.ToString(), released == OutpostReleaseResultEnum.Released ? Color.Green : Color.Yellow);
        return ShellExit.Success;
    }
}
