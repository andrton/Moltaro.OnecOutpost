using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// The settings of install and uninstall: how long they wait for the service to stop (principle 10), in the constant
// form of policy.json, from 1 second to 1 hour, 1 minute when absent.
internal sealed class ServiceSettings : CommandSettings
{
    // The rule of --stop-timeout, for its errors, which never repeat the value given.
    public const string StopTimeoutRule =
        "--stop-timeout: expected a time of the form [d.]hh:mm:ss from 00:00:01 to 01:00:00, such as 00:01:00";

    private static readonly TimeSpan MinStopTimeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxStopTimeout = TimeSpan.FromHours(1);

    [CommandOption("--stop-timeout <TIME>")]
    [Description("How long to wait for the service to stop, of the form [d.]hh:mm:ss, from 00:00:01 to 01:00:00.")]
    [TypeConverter(typeof(StopTimeoutConverter))]
    [DefaultValue(typeof(TimeSpan), "00:01:00")]
    public TimeSpan StopTimeout { get; init; }

    public override ValidationResult Validate()
    {
        if (StopTimeout < MinStopTimeout || StopTimeout > MaxStopTimeout)
        {
            return ValidationResult.Error(StopTimeoutRule);
        }

        return ValidationResult.Success();
    }
}
