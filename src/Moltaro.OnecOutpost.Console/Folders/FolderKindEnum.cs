namespace Moltaro.OnecOutpost.Console.Folders;

// What is at a path: nothing, a folder, or a reparse point, such as a junction, which is never followed.
internal enum FolderKindEnum
{
    Absent,
    Folder,
    ReparsePoint,
}
