using System.ComponentModel;
using Moltaro.OnecOutpost.Protocol;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

internal sealed partial class CheckCommand
{
    // The option of check: the allowed skew between this machine's clock and the server's, in the constant form of
    // policy.json, from OutpostProtocol.MinClockSkew to MaxClockSkew, the server's default ClockSkew when absent, since
    // the console cannot read the server's (principle 10).
    public sealed class Settings : CommandSettings
    {
        // The default, the server's default ClockSkew, which a test compares with OutpostServerOptions.
        public const string DefaultClockSkew = "00:02:00";

        // The rule of --clock-skew, for its errors, which never repeat the value given.
        public const string ClockSkewRule =
            "--clock-skew: expected a time of the form [d.]hh:mm:ss from 00:00:01 to 01:00:00, such as 00:02:00";

        // The help reads a description as markup, so its brackets are doubled.
        [CommandOption("--clock-skew <TIME>")]
        [Description("The allowed skew between this machine's clock and the server's, of the form [[d.]]hh:mm:ss, from 00:00:01 to 01:00:00.")]
        [TypeConverter(typeof(ClockSkewConverter))]
        [DefaultValue(typeof(TimeSpan), DefaultClockSkew)]
        public TimeSpan ClockSkew { get; init; }

        public override ValidationResult Validate()
        {
            if (ClockSkew < OutpostProtocol.MinClockSkew || ClockSkew > OutpostProtocol.MaxClockSkew)
            {
                return ValidationResult.Error(ClockSkewRule);
            }

            return ValidationResult.Success();
        }
    }
}
