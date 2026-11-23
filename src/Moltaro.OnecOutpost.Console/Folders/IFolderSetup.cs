namespace Moltaro.OnecOutpost.Console.Folders;

// The folders that the console writes, with their owners and access lists as NTFS keeps them: the boundary that
// WindowsFolderSetup implements and tests replace. A failure is an IOException or an UnauthorizedAccessException.
internal interface IFolderSetup
{
    // What is at the path.
    FolderKindEnum Kind(string path);

    // The final path of the folder by GetFinalPathNameByHandleW, so that a short name or a subst drive names the same
    // folder; the full path of one that does not exist.
    string FinalPath(string folder);

    // Makes the program folder, or applies its descriptor to the one that exists; with a source, empties it and copies
    // the source's content in; then gives every entry Administrators as its owner and only inherited entries. A null
    // source applies the lists alone.
    void ApplyProgramFolder(string folder, string? source);

    // With create, makes the data folder with its descriptor by CreateDirectoryW, failing when anything is there
    // already; otherwise applies the descriptor to the folder that exists. audit\ and outcomes\ get theirs, made or
    // applied. The agent's SID is in its string form. Files are not touched.
    void ApplyDataFolder(string folder, string agentSid, bool create);

    // Makes Administrators the owner of the file.
    void OwnByAdministrators(string file);
}
