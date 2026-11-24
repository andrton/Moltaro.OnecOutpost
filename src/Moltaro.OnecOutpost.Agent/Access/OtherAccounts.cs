namespace Moltaro.OnecOutpost.Agent.Access;

// Who else an entry of the data folder admits, by its full path: its owner when that is somebody else, since an owner
// can always change the access list, and the other accounts that an allow entry applying to the entry gives any right,
// each once. Accounts are named as Windows translates their SIDs, or by the SID.
internal sealed record OtherAccounts(string FullPath, string? Owner, IReadOnlyList<string> Entries)
{
    // No other owner and no entry.
    public bool IsEmpty => Owner is null && Entries.Count == 0;

    // The owner, then the entries, each once: the list that Others gave before the owner was named apart.
    public IReadOnlyList<string> All => Owner is string owner ? [owner, .. Entries.Where(entry => entry != owner)] : Entries;
}
