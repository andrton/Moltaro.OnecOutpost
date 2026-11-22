namespace Moltaro.OnecOutpost.Agent.Access;

// The access lists of the data folder: the boundary that Windows implements and tests replace.
internal interface IAccessLists
{
    // The accounts, other than Administrators, SYSTEM and the account the agent runs under, that own the entry at a
    // path relative to the data folder, "" for the folder itself, such as audit or audit\2026-11-22.jsonl, or that an
    // allow entry applying to the entry itself gives any right. Empty when there are none; IOException when the list
    // cannot be read.
    IReadOnlyList<string> Others(string path);
}
