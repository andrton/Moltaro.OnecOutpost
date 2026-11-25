namespace Moltaro.OnecOutpost.Console.Commands;

// A category of the metadata census holds its name and counts of objects found, read without error, or failed. A
// failure can belong to the object or to its page. The walk updates the counts from one task at a time.
internal sealed class WalkCategory
{
    public WalkCategory(string name, int found)
    {
        Name = name;
        Found = found;
    }

    public string Name { get; }

    public int Found { get; }

    public int Read { get; set; }

    public int Failed { get; set; }
}
