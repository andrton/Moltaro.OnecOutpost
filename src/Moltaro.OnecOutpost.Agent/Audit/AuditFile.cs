namespace Moltaro.OnecOutpost.Agent.Audit;

// What the audit log keeps of one of its files: its length, so that a failed write can be cut back; the time of its
// last entry, null while it holds none, for retention; and the facts of the commands and the highest reservation of
// each identity that its entries give, so that the log can tell what a file holds and know again what is left after
// retention removed files. The log owns it and changes it; not thread-safe.
internal sealed class AuditFile
{
    public AuditFile(string name)
    {
        Name = name;
    }

    // The name in audit\, YYYY-MM-DD.jsonl.
    public string Name { get; }

    // The length the file has with every entry that counts as written; null when the file could not be read.
    public long? Length { get; set; }

    public DateTimeOffset? LastTime { get; set; }

    // The facts of each command that an entry of this file names, merged in the order of its lines.
    public Dictionary<Guid, AuditCommand> Commands { get; } = [];

    // The greatest below of the reservations of each identity in this file.
    public Dictionary<Guid, long> Reservations { get; } = [];
}
