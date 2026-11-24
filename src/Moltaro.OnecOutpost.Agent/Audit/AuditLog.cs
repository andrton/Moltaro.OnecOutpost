using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Audit;

// The audit log in audit\ (§13 of docs/protocol.md), held through its lock file without sharing while the agent runs.
// It reads the files named YYYY-MM-DD.jsonl at its opening, cutting a torn last line, and adds one entry at a time,
// each with one Append to the file of the UTC day of its time and on the disk before the writer returns. Not
// thread-safe: one task makes every change of the agent's files. A failed write cuts the file back, sets Fault and
// throws AgentStopException; with Fault set, every writer but Started and Stopped writes nothing and throws
// AgentStopException, and after a failed write Started too. Stopped throws nothing: with Fault set it writes nothing.
internal sealed class AuditLog : IDisposable
{
    private const string LockName = "lock";

    // An audit file is named by the UTC day of its entries: YYYY-MM-DD.jsonl.
    private const string DayFormat = "yyyy-MM-dd";

    private const string FileExtension = ".jsonl";

    // How much of a file is read at a time; a line may be longer, since no line has a limit of length.
    private const int ChunkSize = 64 * 1024;

    // A line is parsed with the depth of a message, not the default 64, so that a received whose parameters nest 64
    // levels, 65 inside the entry, is read and not taken for a torn line (§2 of docs/protocol.md).
    private static readonly JsonDocumentOptions EntryOptions = new() { MaxDepth = OutpostProtocol.MaxMessageDepth };

    private readonly IAgentFolder _folder;

    private readonly TimeProvider _clock;

    // The lock file, held without sharing until the log is disposed.
    private readonly IDisposable _held;

    // Every audit file, in the order of their names, with what each holds.
    private readonly SortedList<string, AuditFile> _files = new(StringComparer.Ordinal);

    // The facts of every command that an entry names, over all the files.
    private readonly Dictionary<Guid, AuditCommand> _commands = [];

    // For each command with an ended or a refused, the file of the last one, which gives its kind, code and duration.
    private readonly Dictionary<Guid, string> _outcomeFiles = [];

    // The greatest below of the reservations of each identity, over all the files.
    private readonly Dictionary<Guid, long> _reservations = [];

    private string? _fault;

    // Set by a failed write, after which nothing more is written, not even started.
    private bool _writeFailed;

    private AuditLog(IAgentFolder folder, TimeProvider clock, IDisposable held)
    {
        _folder = folder;
        _clock = clock;
        _held = held;
    }

    // Why the log cannot be trusted, naming the file and the line, or the failed write; null when it can.
    public string? Fault => _fault;

    // The names of the audit files it read.
    public IReadOnlyList<string> Files => [.. _files.Keys];

    // Every command that an entry names and no confirmed does: the received list of a poll (§6.2).
    public IReadOnlyCollection<AuditCommand> Unconfirmed => [.. _commands.Values.Where(command => !command.Confirmed)];

    // Takes the lock of the folder, whose IOException passes and records nothing, then reads every audit file in the
    // order of their names, cuts a last line without its LF or that is not JSON, and parses the lines with the depth of
    // a message. A line it cannot read, or a file it cannot open, sets Fault and ends the reading there.
    public static AuditLog Open(IAgentFolder audit, TimeProvider clock)
    {
        IDisposable held = audit.Lock(LockName);

        try
        {
            AuditLog log = new(audit, clock, held);
            log.ReadFiles();

            return log;
        }
        catch
        {
            held.Dispose();
            throw;
        }
    }

    // The facts of a command that a received, a refused with a command, an ended or a confirmed names; null for any
    // other.
    public AuditCommand? Find(Guid command)
    {
        return _commands.GetValueOrDefault(command);
    }

    // The greatest below of the reservations of that identity, in whichever files they are; null when there is none.
    public long? HighestReservation(Guid agent)
    {
        return _reservations.TryGetValue(agent, out long below) ? below : null;
    }

    // Records started: the agent's version, the keys of the bases that write without safe mode, and the error that
    // stops the start, null when there is none.
    public void Started(string version, IReadOnlyList<string> withoutSafeMode, string? error)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("started");

        writer.Name("version");
        writer.String(version);
        writer.Name("withoutSafeMode");
        writer.Strings(withoutSafeMode);

        if (error is not null)
        {
            writer.Name("error");
            writer.String(error);
        }

        Append(writer, time);
    }

    // Records stopped (the owner's Q16): the reason of a stop of the agent's own after its started, as the
    // AgentStopException that stops the run words it, which names a file by its path and never a script, a parameter, a
    // result or a secret. It throws nothing: with Fault set, a failed write of the log or a log that cannot be read,
    // it writes nothing, and its own failed write is cut back as any entry's; the agent stops either way.
    public void Stopped(string error)
    {
        if (_fault is not null)
        {
            return;
        }

        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("stopped");

        writer.Name("error");
        writer.String(error);

        try
        {
            Append(writer, time);
        }
        catch (AgentStopException)
        {
            // Append cut the file back and set Fault; the stop it would report is the one being recorded.
        }
    }

    // Records policy, valid exactly when the error is null.
    public void Policy(string? error)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("policy");

        writer.Name("valid");
        writer.Boolean(error is null);

        if (error is not null)
        {
            writer.Name("error");
            writer.String(error);
        }

        Append(writer, time);
    }

    // Records connection, connected or lost, with the reason of §9.
    public void Connection(bool connected, string? reason)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("connection");

        writer.Name("state");
        writer.String(connected ? "connected" : "lost");

        if (reason is not null)
        {
            writer.Name("reason");
            writer.String(reason);
        }

        Append(writer, time);
    }

    // Records bases: the bases the agent holds and those it claims.
    public void Bases(IReadOnlyList<string> held, IReadOnlyList<string> claimed)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("bases");

        writer.Name("held");
        writer.Strings(held);
        writer.Name("claimed");
        writer.Strings(claimed);

        Append(writer, time);
    }

    // Records received: the command, its script and its parameters, written again with OutpostJsonWriter.Value.
    public void Received(OutpostCommandMessage command)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("received");

        writer.Name("command");
        writer.Id(command.Id);
        writer.Name("base");
        writer.String(command.Base);
        writer.Name("mode");
        writer.String(OutpostForms.WriteMode(command.Mode));
        writer.Name("callerKey");
        writer.NullableString(command.CallerKey);
        writer.Name("script");
        writer.String(command.Script);
        writer.Name("parameters");
        writer.Value(command.Parameters);
        writer.Name("issued");
        writer.Time(command.Issued);
        writer.Name("deadline");
        writer.Time(command.Deadline);

        AuditFile file = Append(writer, time);
        Apply(file, AuditCommand.OfReceived(command.Id, command.Mode));
    }

    // Records refused with its code: the command, its base and its mode where they are known, none of them for signature
    // and form.
    public void Refused(Guid? command, string? baseKey, OutpostModeEnum? mode, string code)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("refused");

        writer.Name("command");

        if (command is Guid id)
        {
            writer.Id(id);
        }
        else
        {
            writer.Null();
        }

        writer.Name("base");
        writer.NullableString(baseKey);
        writer.Name("mode");
        writer.NullableString(mode is OutpostModeEnum known ? OutpostForms.WriteMode(known) : null);
        writer.Name("code");
        writer.String(code);

        AuditFile file = Append(writer, time);

        if (command is Guid refused)
        {
            Apply(file, AuditCommand.OfRefused(refused, mode, code));
        }
    }

    // Records ended: the outcome succeeded, failed or unknown, its code and its duration, null for one that a start
    // writes.
    public void Ended(Guid command, OutpostAgentOutcomeKindEnum kind, string? code, long? durationMs)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("ended");

        writer.Name("command");
        writer.Id(command);
        writer.Name("outcome");
        writer.String(OutcomeWord(kind));
        writer.Name("code");
        writer.NullableString(code);
        writer.Name("durationMs");
        writer.NullableInteger(durationMs);

        AuditFile file = Append(writer, time);
        Apply(file, AuditCommand.OfEnded(command, kind, code, durationMs));
    }

    // Records confirmed: the server keeps the outcome of the command.
    public void Confirmed(Guid command)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("confirmed");

        writer.Name("command");
        writer.Id(command);

        AuditFile file = Append(writer, time);
        Apply(file, AuditCommand.OfConfirmed(command));
    }

    // Records reservation: the bound below which the identity sends its poll numbers (§6.2).
    public void Reservation(Guid agent, long below)
    {
        (OutpostJsonWriter writer, DateTimeOffset time) = Begin("reservation");

        writer.Name("agent");
        writer.Id(agent);
        writer.Name("below");
        writer.Integer(below);

        AuditFile file = Append(writer, time);
        ApplyReservation(file, agent, below);
    }

    // The whole rule of retention, called after each verified poll answer with its time: nothing when the clock is more
    // than a day away from that time, either way; otherwise it deletes, in the order of their names, every file whose
    // last entry is more than retentionDays plus a day before the clock, and every file with no entry, except a file
    // with an entry of an unconfirmed command and the file of the highest reservation of the identity. No command is
    // left with its received or ended and without its confirmed: a file stays while it holds the confirmed of a command
    // that has an entry in a file that stays, and it is deleted after the other files of that command, while a received
    // or an ended may go before its confirmed. A file it cannot delete stays until a later call, and so does the file
    // of the confirmed of each of its commands; nothing is recorded.
    public void RemoveExpired(Guid agent, DateTimeOffset answerTime, int retentionDays)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        // A clock that a hop or a fault moved far from the server's wipes nothing out.
        if ((now - answerTime).Duration() > TimeSpan.FromDays(1))
        {
            return;
        }

        // With the day of tolerance, a file older than this is still older than the retention by the server's clock.
        DateTimeOffset limit = now - TimeSpan.FromDays(retentionDays + 1);
        long? highest = HighestReservation(agent);
        HashSet<AuditFile> deletable = [];

        foreach (AuditFile file in _files.Values)
        {
            bool expired = file.LastTime is not DateTimeOffset last || last < limit;

            // The log is the journal of rules 3, 10 and 11: a command not confirmed is listed in every poll, and the
            // highest reservation bounds every poll after a restart.
            bool holdsUnconfirmed = file.Commands.Keys.Any(command => !_commands[command].Confirmed);
            bool holdsHighest = file.Reservations.TryGetValue(agent, out long below) && below == highest;

            if (expired && !holdsUnconfirmed && !holdsHighest)
            {
                deletable.Add(file);
            }
        }

        bool removed = false;

        // The first file in the order of names that waits for no other: a file that holds a confirmed goes after the
        // other files of that command, and so stays while one of them stays, so that a stop or a failed delete never
        // leaves a command with its received or ended and without its confirmed.
        while (true)
        {
            AuditFile? next = _files.Values.FirstOrDefault(file => deletable.Contains(file) && !WaitsForOthers(file));

            if (next is null)
            {
                break;
            }

            deletable.Remove(next);

            try
            {
                _folder.Delete(next.Name);
            }
            catch (IOException)
            {
                // The file stays until a later call, and with it the file of the confirmed of each of its commands,
                // which waits for it.
                continue;
            }

            _files.Remove(next.Name);
            removed = true;
        }

        // A command none of whose entries is left is no longer known, and the facts of the others are what the files
        // that are left give.
        if (removed)
        {
            Rebuild();
        }
    }

    // Releases the lock of the folder.
    public void Dispose()
    {
        _held.Dispose();
    }

    // Whether the name is that of a file of the log, {yyyy-MM-dd}.jsonl, for the start and for the console's check,
    // which judge the access list of each.
    public static bool IsAuditFile(string name)
    {
        return name.Length == DayFormat.Length + FileExtension.Length
            && name.EndsWith(FileExtension, StringComparison.Ordinal)
            && DateTime.TryParseExact(
                name.AsSpan(0, DayFormat.Length),
                DayFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
    }

    // The lines of a file, each to its LF, which is not part of its bytes, with the offset at which it starts. Only the
    // last line can come without its LF.
    private static IEnumerable<(long Start, byte[] Bytes, bool Ended)> Lines(Stream stream)
    {
        byte[] chunk = new byte[ChunkSize];
        ArrayBufferWriter<byte> line = new();
        long start = 0;
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            int from = 0;

            while (from < read)
            {
                int end = Array.IndexOf(chunk, (byte)'\n', from, read - from);

                if (end < 0)
                {
                    line.Write(chunk.AsSpan(from, read - from));
                    break;
                }

                line.Write(chunk.AsSpan(from, end - from));

                byte[] bytes = line.WrittenSpan.ToArray();
                line.ResetWrittenCount();

                yield return (start, bytes, true);

                start += bytes.Length + 1;
                from = end + 1;
            }
        }

        if (line.WrittenCount > 0)
        {
            byte[] torn = line.WrittenSpan.ToArray();

            yield return (start, torn, false);
        }
    }

    // The entry of a line, or null for a line that is not JSON. The caller disposes the document.
    private static JsonDocument? Parse(byte[] line)
    {
        try
        {
            return JsonDocument.Parse(line, EntryOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(byte[] line)
    {
        using JsonDocument? document = Parse(line);

        return document is not null;
    }

    // Merges facts of one command, the later over the earlier: every fact is a set or a maximum but the kind, the code
    // and the duration, which the later gives when it has them and laterOutcome says that its ended or refused counts
    // as the last.
    private static AuditCommand Merge(AuditCommand? earlier, AuditCommand later, bool laterOutcome)
    {
        if (earlier is null)
        {
            return later;
        }

        AuditCommand outcome = laterOutcome && later.Kind is not null ? later : earlier;

        return new AuditCommand(
            later.Id,
            later.Mode ?? earlier.Mode,
            earlier.Received || later.Received,
            outcome.Kind,
            outcome.Code,
            outcome.DurationMs,
            earlier.Confirmed || later.Confirmed);
    }

    // The outcome of an ended: succeeded, failed or unknown, never refused, which a refused records.
    private static string OutcomeWord(OutpostAgentOutcomeKindEnum kind)
    {
        if (kind == OutpostAgentOutcomeKindEnum.Refused)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An ended has no outcome of this kind.");
        }

        return OutpostForms.WriteOutcomeKind(kind);
    }

    // Reading an entry: each reader takes the member of that name only when the entry gives it exactly once, in its
    // form. The log reads time of every line; command and mode of received; command, mode and code of refused; command,
    // outcome, code and durationMs of ended; command of confirmed; agent and below of reservation; error of stopped,
    // a string; nothing else.
    private static bool TryReadTime(JsonElement entry, out DateTimeOffset time)
    {
        time = default;

        return TryReadText(entry, "time", out string? text) && OutpostForms.TryReadTime(text, out time);
    }

    private static bool TryReadText(JsonElement entry, string name, [NotNullWhen(true)] out string? text)
    {
        text = null;

        return OutpostJsonReader.TryGetMember(entry, name, out JsonElement value)
            && OutpostJsonReader.TryReadText(value, out text);
    }

    private static bool TryReadNullableText(JsonElement entry, string name, out string? text)
    {
        text = null;

        if (!OutpostJsonReader.TryGetMember(entry, name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Null || OutpostJsonReader.TryReadText(value, out text);
    }

    private static bool TryReadId(JsonElement entry, string name, out Guid id)
    {
        id = Guid.Empty;

        return OutpostJsonReader.TryGetMember(entry, name, out JsonElement value)
            && OutpostJsonReader.TryReadId(value, out id);
    }

    private static bool TryReadNullableId(JsonElement entry, string name, out Guid? id)
    {
        id = null;

        if (!OutpostJsonReader.TryGetMember(entry, name, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        bool read = OutpostJsonReader.TryReadId(value, out Guid known);
        id = known;

        return read;
    }

    // read or write; null where the entry allows null.
    private static bool TryReadMode(JsonElement entry, out OutpostModeEnum? mode)
    {
        mode = null;

        if (!TryReadNullableText(entry, "mode", out string? word))
        {
            return false;
        }

        mode = OutpostForms.ReadMode(word);

        return word is null || mode is not null;
    }

    private static bool TryReadOutcome(JsonElement entry, out OutpostAgentOutcomeKindEnum kind)
    {
        kind = default;

        if (!TryReadText(entry, "outcome", out string? word)
            || OutpostForms.ReadOutcomeKind(word) is not OutpostAgentOutcomeKindEnum read
            || read == OutpostAgentOutcomeKindEnum.Refused)
        {
            return false;
        }

        kind = read;

        return true;
    }

    // A duration of §2, whole milliseconds, or null.
    private static bool TryReadDuration(JsonElement entry, out long? durationMs)
    {
        durationMs = null;

        if (!OutpostJsonReader.TryGetMember(entry, "durationMs", out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        bool read = OutpostJsonReader.TryReadInteger(value, out long milliseconds) && milliseconds >= 0;
        durationMs = milliseconds;

        return read;
    }

    private static bool TryReadInteger(JsonElement entry, string name, out long integer)
    {
        integer = 0;

        return OutpostJsonReader.TryGetMember(entry, name, out JsonElement value)
            && OutpostJsonReader.TryReadInteger(value, out integer);
    }

    private void ReadFiles()
    {
        foreach (string name in _folder.Names().Where(IsAuditFile).Order(StringComparer.Ordinal))
        {
            AuditFile file = new(name);
            _files.Add(name, file);

            ReadFile(file);
        }
    }

    // Reads one file line by line. A last line that lacks its LF, or that has it and is not JSON, is a write that never
    // completed: it is cut off, whatever the lines before it hold, and the agent went on from no such line. Every other
    // line is taken as an entry until one is not; after that, and once any file set the fault, lines are read only so
    // that the torn last line of every file is cut.
    private void ReadFile(AuditFile file)
    {
        (long Start, byte[] Bytes, bool Ended)? last = null;
        int number = 0;

        try
        {
            using Stream stream = _folder.OpenRead(file.Name);

            foreach ((long Start, byte[] Bytes, bool Ended) line in Lines(stream))
            {
                // Another line follows this one, so it is not the last.
                if (last is { } before)
                {
                    Take(file, before.Bytes, number);
                }

                last = line;
                number++;
            }
        }
        catch (IOException)
        {
            _fault ??= $@"the audit log cannot be read: audit\{file.Name} cannot be opened or read";

            return;
        }

        if (last is not { } final)
        {
            file.Length = 0;

            return;
        }

        if (!final.Ended || !IsJson(final.Bytes))
        {
            // Its IOException passes: a log whose torn line stays cannot be written after it.
            _folder.Cut(file.Name, final.Start);
            file.Length = final.Start;

            return;
        }

        Take(file, final.Bytes, number);
        file.Length = final.Start + final.Bytes.Length + 1;
    }

    // Takes a whole line as an entry, or sets the fault, naming the file and the line by its number from 1 and quoting
    // nothing of it. With the fault set, it takes nothing: reading stops at the first line that is not an entry.
    private void Take(AuditFile file, byte[] line, int number)
    {
        if (_fault is not null)
        {
            return;
        }

        using JsonDocument? document = Parse(line);

        if (document is null || !TryApply(file, document.RootElement))
        {
            _fault = $@"the audit log cannot be read: line {number} of audit\{file.Name} is not an entry of the log";
        }
    }

    // Applies an entry, an object with time in the form of §2 and an entry of the table whose members that the log
    // reads are in their forms; false for anything else.
    private bool TryApply(AuditFile file, JsonElement entry)
    {
        if (!TryReadTime(entry, out DateTimeOffset time) || !TryReadText(entry, "entry", out string? kind))
        {
            return false;
        }

        switch (kind)
        {
            case "started":
            case "policy":
            case "connection":
            case "bases":
                break;

            case "received":
                if (!TryReadId(entry, "command", out Guid received)
                    || !TryReadMode(entry, out OutpostModeEnum? mode)
                    || mode is null)
                {
                    return false;
                }

                Apply(file, AuditCommand.OfReceived(received, mode.Value));
                break;

            case "refused":
                if (!TryReadNullableId(entry, "command", out Guid? refused)
                    || !TryReadMode(entry, out OutpostModeEnum? refusedMode)
                    || !TryReadText(entry, "code", out string? refusal))
                {
                    return false;
                }

                // A refusal without an identity, for signature or form, names no command.
                if (refused is Guid id)
                {
                    Apply(file, AuditCommand.OfRefused(id, refusedMode, refusal));
                }

                break;

            case "ended":
                if (!TryReadId(entry, "command", out Guid ended)
                    || !TryReadOutcome(entry, out OutpostAgentOutcomeKindEnum outcome)
                    || !TryReadNullableText(entry, "code", out string? code)
                    || !TryReadDuration(entry, out long? durationMs))
                {
                    return false;
                }

                Apply(file, AuditCommand.OfEnded(ended, outcome, code, durationMs));
                break;

            case "confirmed":
                if (!TryReadId(entry, "command", out Guid confirmed))
                {
                    return false;
                }

                Apply(file, AuditCommand.OfConfirmed(confirmed));
                break;

            case "reservation":
                if (!TryReadId(entry, "agent", out Guid agent) || !TryReadInteger(entry, "below", out long below))
                {
                    return false;
                }

                ApplyReservation(file, agent, below);
                break;

            case "stopped":
                if (!TryReadText(entry, "error", out _))
                {
                    return false;
                }

                break;

            default:
                return false;
        }

        file.LastTime = time;

        return true;
    }

    // Starts an entry with its time from the clock, truncated to the millisecond as §2 writes it, and its kind. With a
    // fault only started may be written, and after a failed write nothing.
    private (OutpostJsonWriter Writer, DateTimeOffset Time) Begin(string entry)
    {
        if (_writeFailed || (_fault is not null && entry != "started"))
        {
            throw new AgentStopException(_fault!);
        }

        DateTimeOffset time = OutpostForms.ToMilliseconds(_clock.GetUtcNow());

        OutpostJsonWriter writer = new();
        writer.StartObject();
        writer.Name("time");
        writer.Time(time);
        writer.Name("entry");
        writer.String(entry);

        return (writer, time);
    }

    // Ends the entry and adds it with its LF to the file of the UTC day of its time, with one Append that is on the
    // disk when it returns. A failed write is cut back to the length the file had before it, so that the next start
    // finds the log as it was; an exception of that cut is not reported, since every entry is safe to find written at
    // the next start, which cuts a torn one. Nothing more is written then, and the agent stops.
    private AuditFile Append(OutpostJsonWriter writer, DateTimeOffset time)
    {
        writer.EndObject();

        byte[] line = [.. writer.ToArray(), (byte)'\n'];
        AuditFile file = FileOf(time);

        try
        {
            _folder.Append(file.Name, line);
        }
        catch (IOException)
        {
            _writeFailed = true;
            _fault = $@"the audit log cannot be written: an entry of audit\{file.Name} failed";

            CutBack(file);

            throw new AgentStopException(_fault);
        }

        file.Length += line.Length;
        file.LastTime = time;

        return file;
    }

    private void CutBack(AuditFile file)
    {
        if (file.Length is not long length)
        {
            return;
        }

        try
        {
            _folder.Cut(file.Name, length);
        }
        catch (IOException)
        {
            // Not reported: the next start cuts the torn entry, or finds it whole.
        }
    }

    // The file of the UTC day of a time. A file that the log does not know yet is new, since the log read every audit
    // file when it opened and only the agent writes them.
    private AuditFile FileOf(DateTimeOffset time)
    {
        string name = time.UtcDateTime.ToString(DayFormat, CultureInfo.InvariantCulture) + FileExtension;

        if (!_files.TryGetValue(name, out AuditFile? file))
        {
            file = new AuditFile(name) { Length = 0 };
            _files.Add(name, file);
        }

        return file;
    }

    // The facts of one entry, which is the last of its file, go into what the file holds and into what the log gives.
    private void Apply(AuditFile file, AuditCommand facts)
    {
        file.Commands[facts.Id] = Merge(file.Commands.GetValueOrDefault(facts.Id), facts, laterOutcome: true);

        MergeIntoLog(file.Name, facts);
    }

    // Only the kind, the code and the duration depend on the order of the files: they come from the last ended or
    // refused in the order of file names and lines. An entry in a file named before the file of the last one, written
    // after the clock was set back, does not replace it, as a restart would not read it so.
    private void MergeIntoLog(string fileName, AuditCommand facts)
    {
        bool laterOutcome = facts.Kind is not null
            && (!_outcomeFiles.TryGetValue(facts.Id, out string? outcomeFile)
                || string.CompareOrdinal(fileName, outcomeFile) >= 0);

        _commands[facts.Id] = Merge(_commands.GetValueOrDefault(facts.Id), facts, laterOutcome);

        if (laterOutcome)
        {
            _outcomeFiles[facts.Id] = fileName;
        }
    }

    private void ApplyReservation(AuditFile file, Guid agent, long below)
    {
        if (!file.Reservations.TryGetValue(agent, out long known) || below > known)
        {
            file.Reservations[agent] = below;
        }

        MergeReservation(agent, below);
    }

    private void MergeReservation(Guid agent, long below)
    {
        if (!_reservations.TryGetValue(agent, out long known) || below > known)
        {
            _reservations[agent] = below;
        }
    }

    // True while another file that is left holds an entry of a command whose confirmed this file holds. Only that
    // direction keeps a file: a received or an ended may go while its confirmed stays, so a file that is never past the
    // retention keeps no older one.
    private bool WaitsForOthers(AuditFile file)
    {
        foreach (AuditCommand facts in file.Commands.Values.Where(facts => facts.Confirmed))
        {
            if (_files.Values.Any(other => other != file && other.Commands.ContainsKey(facts.Id)))
            {
                return true;
            }
        }

        return false;
    }

    // What the log gives, made again from the files that are left, in the order of their names.
    private void Rebuild()
    {
        _commands.Clear();
        _outcomeFiles.Clear();
        _reservations.Clear();

        foreach (AuditFile file in _files.Values)
        {
            foreach (AuditCommand facts in file.Commands.Values)
            {
                MergeIntoLog(file.Name, facts);
            }

            foreach ((Guid agent, long below) in file.Reservations)
            {
                MergeReservation(agent, below);
            }
        }
    }
}
