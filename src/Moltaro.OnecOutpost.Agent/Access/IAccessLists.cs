namespace Moltaro.OnecOutpost.Agent.Access;

// The access lists of the data folder and of the program folder: the boundary that Windows implements and tests
// replace.
internal interface IAccessLists
{
    // The accounts, other than Administrators, SYSTEM and the account the agent runs under, that own the entry at a
    // path relative to the data folder, "" for the folder itself, such as audit or audit\2026-11-22.jsonl, or that an
    // allow entry applying to the entry itself gives any right. Empty when there are none; IOException when the list
    // cannot be read.
    IReadOnlyList<string> Others(string path);

    // The entries, by full path, with their accounts named as Others names them, through which an account other than
    // Administrators, SYSTEM, NT SERVICE\TrustedInstaller and the agent's account could change the code that the agent's
    // account runs from the folder, a full path: each entry that such an account owns, or that an allow entry applying
    // to the entry itself gives
    // - on each parent, from the volume's root down, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions,
    //   TakeOwnership or GenericAll; adding a subfolder, which C:\ gives Authenticated Users, plants a name only after
    //   one of these, so it is not judged by itself;
    // - on the folder and everything in it, a folder before its entries by ordinal name, any right beyond
    //   ReadAndExecute, Synchronize and the generic rights to read and to run; a reparse point is judged by its own list
    //   and not entered.
    // Empty when there are none; IOException when a list cannot be read or a folder listed.
    IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder);
}
