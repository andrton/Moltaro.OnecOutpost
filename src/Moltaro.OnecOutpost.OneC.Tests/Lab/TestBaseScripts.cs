namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// Scripts that more than one test sends to the test base, in the English variant of the language and with only what
// the compatibility mode 8.2.13 of the test configuration allows. Each test marks what it writes with a description
// of its own, so that it finds its data and only its data, whatever earlier runs left in the base.
internal static class TestBaseScripts
{
    // Writes one product of the catalog Products with the description of the parameters.
    public const string CreateProduct = """
        Product = Catalogs.Products.CreateItem();
        Product.Description = Parameters["description"];
        Product.Write();
        """;

    // Returns how many products have the description of the parameters.
    public const string CountProducts = """
        Query = New Query;
        Query.Text = "SELECT COUNT(*) AS ProductCount FROM Catalog.Products AS Products WHERE Products.Description = &Description";
        Query.SetParameter("Description", Parameters["description"]);
        Selection = Query.Execute().Select();
        Selection.Next();
        Result = Selection.ProductCount;
        """;

    public static string NewDescription()
    {
        return $"outpost-test {Guid.NewGuid():N}";
    }
}
