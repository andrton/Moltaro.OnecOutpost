namespace Moltaro.OnecOutpost.Agent.Tests.FakeFiles;

// One change of a folder of the data folder in memory, as a line of the list that the folders of one data folder share:
// "append {path}", "cut {path} to {length}", "replace {path}" or "delete {path}", the path taken in the data folder,
// such as audit\2026-11-22.jsonl. Bytes are those that an append or a replace wrote, the first ones only of a write
// that failed; empty for a cut and a removal. An operation of a fake worker is a line of the same list, with no bytes:
// "start {worker}", "open {worker}", "run {worker}" or "kill {worker}" (FakeWorkerProcess).
internal sealed record FolderChange(string Line, byte[] Bytes)
{
    public override string ToString()
    {
        return Line;
    }
}
