using System.Globalization;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests;

// Lines of the audit log written by hand, as an earlier run of the agent left them, for a test to seed: each is one
// entry of the table of "The audit log" in the plan, in the form of the writer of §2 of docs/protocol.md, with its LF.
// A received names the base accounting, and a Write the caller key invoice-42.
internal static class AuditLines
{
    // The audit file of the UTC day of a time.
    public static string FileOf(DateTimeOffset time)
    {
        return $"{time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.jsonl";
    }

    // A time in the form of §2.
    public static string Time(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public static string Started(DateTimeOffset time)
    {
        return Line(time, "started", "\"version\":\"0.1.0\",\"withoutSafeMode\":[]");
    }

    public static string Connection(DateTimeOffset time)
    {
        return Line(time, "connection", "\"state\":\"connected\"");
    }

    // A received; the script and the parameters are written into the line as given, so a test gives them in JSON.
    public static string Received(
        DateTimeOffset time,
        Guid command,
        OutpostModeEnum mode,
        string parameters = "{}",
        string script = "Result = 1;")
    {
        string callerKey = mode == OutpostModeEnum.Write ? "\"invoice-42\"" : "null";

        return Line(
            time,
            "received",
            $"\"command\":\"{command}\",\"base\":\"accounting\",\"mode\":\"{Mode(mode)}\",\"callerKey\":{callerKey},"
                + $"\"script\":\"{script}\",\"parameters\":{parameters},\"issued\":\"{Time(time)}\","
                + $"\"deadline\":\"{Time(time + TimeSpan.FromSeconds(30))}\"");
    }

    // A refused; one without a command, as for signature and form, names no base and no mode either.
    public static string Refused(DateTimeOffset time, Guid? command, OutpostModeEnum? mode, string code)
    {
        string identity = command is Guid id ? $"\"{id}\"" : "null";
        string baseKey = command is null ? "null" : "\"accounting\"";
        string modeWord = mode is OutpostModeEnum known ? $"\"{Mode(known)}\"" : "null";

        return Line(
            time,
            "refused",
            $"\"command\":{identity},\"base\":{baseKey},\"mode\":{modeWord},\"code\":\"{code}\"");
    }

    // An ended, with its outcome as the log writes it: succeeded, failed or unknown.
    public static string Ended(DateTimeOffset time, Guid command, string outcome, string? code, long? durationMs)
    {
        string codeText = code is null ? "null" : $"\"{code}\"";
        string duration = durationMs?.ToString(CultureInfo.InvariantCulture) ?? "null";

        return Line(
            time,
            "ended",
            $"\"command\":\"{command}\",\"outcome\":\"{outcome}\",\"code\":{codeText},\"durationMs\":{duration}");
    }

    public static string Confirmed(DateTimeOffset time, Guid command)
    {
        return Line(time, "confirmed", $"\"command\":\"{command}\"");
    }

    public static string Reservation(DateTimeOffset time, Guid agent, long below)
    {
        return Line(
            time,
            "reservation",
            $"\"agent\":\"{agent}\",\"below\":{below.ToString(CultureInfo.InvariantCulture)}");
    }

    // A stopped (the owner's Q16): the reason of a stop after started, its \ and " escaped as the writer of §2 escapes
    // them.
    public static string Stopped(DateTimeOffset time, string error)
    {
        string escaped = error.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        return Line(time, "stopped", $"\"error\":\"{escaped}\"");
    }

    private static string Mode(OutpostModeEnum mode)
    {
        return mode == OutpostModeEnum.Read ? "read" : "write";
    }

    private static string Line(DateTimeOffset time, string entry, string members)
    {
        return $"{{\"time\":\"{Time(time)}\",\"entry\":\"{entry}\",{members}}}\n";
    }
}
