using System.ComponentModel;
using Moltaro.OnecOutpost.Protocol;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

internal sealed partial class MetadataCommand
{
    // The arguments of metadata: a base key by its rule, with secret's sentence, and the path of the report, which may
    // not be empty; that it names a new file, in a folder that exists and outside the data folder, is the command's
    // second step, after the elevation.
    public sealed class Settings : CommandSettings
    {
        // The rule of an empty <FILE>.
        public const string FileRule = "<FILE>: expected the path of a new file";

        [CommandArgument(0, "<BASE>")]
        [Description("The key of the base in policy.json.")]
        public string Base { get; init; } = "";

        [CommandArgument(1, "<FILE>")]
        [Description("The path of the report to write, a JSON file that does not exist yet.")]
        public string File { get; init; } = "";

        public override ValidationResult Validate()
        {
            if (!OutpostNames.IsBaseKey(Base))
            {
                return ValidationResult.Error(SecretCommand.Settings.BaseRule);
            }

            if (string.IsNullOrWhiteSpace(File))
            {
                return ValidationResult.Error(FileRule);
            }

            return ValidationResult.Success();
        }
    }
}
