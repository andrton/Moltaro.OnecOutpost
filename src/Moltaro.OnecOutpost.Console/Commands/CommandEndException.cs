using System.ComponentModel;

namespace Moltaro.OnecOutpost.Console.Commands;

// A command ends before its last step: its exit code, one of ConsoleExitCodes, and the reason that ConsoleApp writes in
// the line of its end, {Kind} ({code}): {reason}. A step throws it, so that each command reads as its steps in their
// order and nothing runs after the one that ended it. The reason never holds a password, a token or text that the
// console has not verified.
internal sealed class CommandEndException : Exception
{
    public CommandEndException(int code, string reason)
        : base(reason)
    {
        Code = code;
    }

    public int Code { get; }

    // A step that failed for a reason of Windows or of the disk.
    public static CommandEndException StepFailed(string step, Exception exception)
    {
        return new CommandEndException(ConsoleExitCodes.Failure, $"the step \"{step}\" failed: {ReasonOf(exception)}");
    }

    // The message of a failure, and (Windows error {code}) for an error of Windows, thrown as it is or inside the
    // IOException of a boundary whose failures are IOExceptions.
    public static string ReasonOf(Exception exception)
    {
        if ((exception as Win32Exception ?? exception.InnerException as Win32Exception) is Win32Exception windows)
        {
            return $"{exception.Message} (Windows error {windows.NativeErrorCode})";
        }

        return exception.Message;
    }
}
