using System.ComponentModel;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal class BaseSettings : CommandSettings
{
    [CommandArgument(0, "<BASE>")]
    [Description("The base key from the agent's local policy.")]
    public string Base { get; init; } = "";
}
