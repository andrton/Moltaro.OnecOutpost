using System.ComponentModel;
using Moltaro.OnecOutpost.Protocol;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

internal sealed partial class SecretCommand
{
    // The arguments of secret: a base key by its rule, and the kind of the password, exactly read, write or worker.
    public sealed class Settings : CommandSettings
    {
        // The rule of <BASE>, which metadata gives too.
        public const string BaseRule =
            "<BASE>: expected a base key: 1 to 64 characters of a-z, 0-9, -, _ and ., the first a letter or a digit";

        [CommandArgument(0, "<BASE>")]
        [Description("The key of the base in policy.json.")]
        public string Base { get; init; } = "";

        [CommandArgument(1, "<KIND>")]
        [Description("read or write for the 1C account of that mode, worker for the Windows account of the base's workers.")]
        public string Kind { get; init; } = "";

        public override ValidationResult Validate()
        {
            if (!OutpostNames.IsBaseKey(Base))
            {
                return ValidationResult.Error(BaseRule);
            }

            if (Kind is not ("read" or "write" or "worker"))
            {
                return ValidationResult.Error("<KIND>: expected read, write or worker");
            }

            return ValidationResult.Success();
        }
    }
}
