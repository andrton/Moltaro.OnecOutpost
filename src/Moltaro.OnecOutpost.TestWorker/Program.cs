using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.TestWorker;

// The test program that stands in for the COM worker in the agent's tests with Requires=Windows (the S9b brief).
// It speaks the worker's lines, command line and exit codes over its raw standard streams, one JSON object per line in
// UTF-8 ended with LF, and runs no 1C:
// - the command line is <base key> <read|write>, and anything else ends it with exit code 1;
// - the end of its input ends it with exit code 0, before the first line and after an outcome;
// - a line it cannot read ends it with exit code 1;
// - the location of the connection line decides the ready line: line <text> gets <text> as its ready line, and any
//   other {"ready":true};
// - each command line runs its script, and each outcome is the executor's line, {"size":<number>,"outcome":<object>},
//   with the true size of the object.
//
// The scripts: wait writes nothing until the input ends or a kill ends the program; exit <n> ends it with n and no
// outcome; line <text> writes <text> as the line of its outcome; diagnostics <text> writes <text> and LF to its
// standard error, as the COM worker writes its diagnostics, and succeeds with null; pid, path, arguments and connection
// succeed with the identity of the process, its Environment.ProcessPath, its command line as an array and the members of
// its connection line as an object; job succeeds with the process ids of the immediate job of the process when its Main began, which
// Main asks before anything else, as an array, or null when the process was in no job, so that a test tells the job of
// the launcher from any other, such as one that the test host runs in; desktop succeeds with the window station and the
// desktop that the process runs on, as "{station}\{desktop}", the form of lpDesktop; user succeeds with the SID of the
// account the process runs under; handle <n> succeeds with the final path of the file that the handle of value n names
// in this process, or null when n names no file here, so that a test sees whether a handle of its own was inherited;
// any other script succeeds with null. The program runs only on Windows.
[SupportedOSPlatform("windows")]
internal static partial class Program
{
    private const string LinePrefix = "line ";

    private const string ExitPrefix = "exit ";

    private const string DiagnosticsPrefix = "diagnostics ";

    private const string HandlePrefix = "handle ";

    // QueryInformationJobObject's class of the process ids of a job, and the error of a list too long for its buffer.
    private const int JobObjectBasicProcessIdList = 3;

    private const int ErrorMoreData = 234;

    // The process ids of the immediate job of the process when Main began; null when it was in no job.
    private static long[]? _jobAtStart;

    private static int Main(string[] arguments)
    {
        // First of all, before the command line is read: the process ids of the immediate job of the process itself.
        _jobAtStart = ImmediateJob();

        if (arguments is not [{ Length: > 0 }, "read" or "write"])
        {
            return 1;
        }

        // The streams live as long as the process, so nothing disposes of them.
        Stream input = new BufferedStream(Console.OpenStandardInput());
        Stream output = Console.OpenStandardOutput();

        byte[]? connection = ReadLine(input);

        if (connection is null)
        {
            return 0;
        }

        string? location = StringMember(connection, "location");

        if (location is null)
        {
            return 1;
        }

        bool ownReady = location.StartsWith(LinePrefix, StringComparison.Ordinal);

        WriteLine(output, ownReady ? location[LinePrefix.Length..] : """{"ready":true}""");

        while (true)
        {
            byte[]? line = ReadLine(input);

            if (line is null)
            {
                return 0;
            }

            string? script = StringMember(line, "script");

            if (script is null)
            {
                return 1;
            }

            if (script == "wait")
            {
                // Nothing is written; what the input still brings is read and dropped until it ends.
                while (ReadLine(input) is not null)
                {
                }

                return 0;
            }

            if (script.StartsWith(ExitPrefix, StringComparison.Ordinal))
            {
                return int.Parse(script[ExitPrefix.Length..], CultureInfo.InvariantCulture);
            }

            if (script.StartsWith(LinePrefix, StringComparison.Ordinal))
            {
                WriteLine(output, script[LinePrefix.Length..]);

                continue;
            }

            if (script.StartsWith(DiagnosticsPrefix, StringComparison.Ordinal))
            {
                Stream error = Console.OpenStandardError();
                error.Write(Encoding.UTF8.GetBytes(script[DiagnosticsPrefix.Length..] + "\n"));
                error.Flush();
            }

            WriteLine(output, OutcomeLine(script, arguments, connection));
        }
    }

    // The next line of the input without its LF, or null when the input ended before a byte of it. A last line
    // without its LF is a line.
    private static byte[]? ReadLine(Stream input)
    {
        ArrayBufferWriter<byte> line = new();

        while (true)
        {
            int next = input.ReadByte();

            if (next < 0)
            {
                return line.WrittenCount == 0 ? null : line.WrittenSpan.ToArray();
            }

            if (next == '\n')
            {
                return line.WrittenSpan.ToArray();
            }

            line.Write([(byte)next]);
        }
    }

    // The string member of a line that is a JSON object, or null when the line is not one or has no such string.
    private static string? StringMember(byte[] line, string name)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The executor's line of a script that succeeds with what it gives: {"size":<number>,"outcome":<object>}.
    private static string OutcomeLine(string script, string[] arguments, byte[] connection)
    {
        ArrayBufferWriter<byte> outcome = new();

        using (Utf8JsonWriter writer = new(outcome))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "succeeded");
            writer.WriteNull("code");
            writer.WritePropertyName("result");
            WriteResult(writer, script, arguments, connection);
            writer.WriteNull("errorText");
            writer.WriteStartArray("messages");
            writer.WriteEndArray();
            writer.WriteBoolean("messagesTrimmed", false);
            writer.WriteEndObject();
        }

        ArrayBufferWriter<byte> line = new();

        using (Utf8JsonWriter writer = new(line))
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", outcome.WrittenCount);
            writer.WritePropertyName("outcome");
            writer.WriteRawValue(outcome.WrittenSpan, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(line.WrittenSpan);
    }

    private static void WriteResult(Utf8JsonWriter writer, string script, string[] arguments, byte[] connection)
    {
        switch (script)
        {
            case "pid":
                writer.WriteNumberValue(Environment.ProcessId);
                break;

            case "path":
                writer.WriteStringValue(Environment.ProcessPath);
                break;

            case "arguments":
                writer.WriteStartArray();

                foreach (string argument in arguments)
                {
                    writer.WriteStringValue(argument);
                }

                writer.WriteEndArray();
                break;

            case "connection":
                writer.WriteRawValue(connection);
                break;

            case "job":
                WriteIds(writer, _jobAtStart);
                break;

            case "user":
                WriteUser(writer);
                break;

            case "desktop":
                WriteDesktop(writer);
                break;

            case string when script.StartsWith(HandlePrefix, StringComparison.Ordinal):
                WriteFinalPath(writer, long.Parse(script[HandlePrefix.Length..], CultureInfo.InvariantCulture));
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    // The process ids of the job with which QueryInformationJobObject answers for no job handle: the immediate job of
    // the calling process, the innermost of nested ones. Null when it fails otherwise than for a buffer too small, as
    // for a process in no job.
    private static unsafe long[]? ImmediateJob()
    {
        for (int capacity = 64; ; capacity *= 2)
        {
            // JOBOBJECT_BASIC_PROCESS_ID_LIST: NumberOfAssignedProcesses and NumberOfProcessIdsInList, two DWORDs, then
            // the ids as ULONG_PTR, from byte 8 on either bitness.
            byte[] list = new byte[(2 * sizeof(uint)) + (capacity * IntPtr.Size)];

            fixed (byte* start = list)
            {
                if (QueryInformationJobObject(IntPtr.Zero, JobObjectBasicProcessIdList, start, (uint)list.Length, IntPtr.Zero))
                {
                    uint count = *(uint*)(start + sizeof(uint));
                    nuint* ids = (nuint*)(start + (2 * sizeof(uint)));
                    long[] given = new long[count];

                    for (int index = 0; index < count; index++)
                    {
                        given[index] = (long)ids[index];
                    }

                    return given;
                }
            }

            if (Marshal.GetLastPInvokeError() != ErrorMoreData)
            {
                return null;
            }
        }
    }

    private static void WriteIds(Utf8JsonWriter writer, long[]? ids)
    {
        if (ids is null)
        {
            writer.WriteNullValue();

            return;
        }

        writer.WriteStartArray();

        foreach (long id in ids)
        {
            writer.WriteNumberValue(id);
        }

        writer.WriteEndArray();
    }

    // The window station of the process and the desktop of its first thread, as "{station}\{desktop}", or null when
    // Windows cannot name either. The handles that Windows gives for them are not to be closed.
    private static void WriteDesktop(Utf8JsonWriter writer)
    {
        string? station = NameOf(GetProcessWindowStation());
        string? desktop = NameOf(GetThreadDesktop(GetCurrentThreadId()));

        if (station is null || desktop is null)
        {
            writer.WriteNullValue();

            return;
        }

        writer.WriteStringValue($"{station}\\{desktop}");
    }

    // The name of a window station or a desktop: GetUserObjectInformationW with UOI_NAME.
    private static unsafe string? NameOf(IntPtr userObject)
    {
        const int NameIndex = 2;

        char[] name = new char[256];
        uint needed;

        fixed (char* start = name)
        {
            if (userObject == IntPtr.Zero
                || !GetUserObjectInformationW(userObject, NameIndex, start, (uint)(name.Length * sizeof(char)), out needed))
            {
                return null;
            }
        }

        // The length counts bytes, the closing null included.
        return new string(name, 0, Math.Max(0, ((int)needed / sizeof(char)) - 1));
    }

    // The SID of the account that the process runs under. The program runs only on Windows.
    private static void WriteUser(Utf8JsonWriter writer)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        writer.WriteStringValue(identity.User!.Value);
    }

    // The final path of the file that the handle names, or null when the handle names no file in this process: no
    // handle of that value, or one of something other than a file.
    private static unsafe void WriteFinalPath(Utf8JsonWriter writer, long handle)
    {
        char[] path = new char[32768];
        uint length;

        fixed (char* start = path)
        {
            length = GetFinalPathNameByHandleW((IntPtr)handle, start, (uint)path.Length, 0);
        }

        if (length == 0 || length >= path.Length)
        {
            writer.WriteNullValue();

            return;
        }

        writer.WriteStringValue(new string(path, 0, (int)length));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryInformationJobObject(
        IntPtr job,
        int informationClass,
        void* information,
        uint length,
        IntPtr returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleW(IntPtr file, char* path, uint length, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetProcessWindowStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetThreadDesktop(uint thread);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(IntPtr userObject, int index, char* information, uint length, out uint needed);

    // The line with its LF in one write, as the worker writes one.
    private static void WriteLine(Stream output, string line)
    {
        output.Write(Encoding.UTF8.GetBytes(line + "\n"));
        output.Flush();
    }
}
