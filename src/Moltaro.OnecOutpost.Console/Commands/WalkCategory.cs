namespace Moltaro.OnecOutpost.Console.Commands;

// One category of the census of metadata (the S10 brief, "The report"): its name, how many objects the census found in
// it, how many the walk read without an error, and how many failed, with an error of their own or in a page that
// failed. The walk counts them, from one task at a time.
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
