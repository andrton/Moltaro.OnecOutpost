using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server.Tests.Commands;

// Parameters nested a given number of levels, the outer object being level 1: each object but the innermost, which is
// empty, holds the next under the name "level". The protocol lets a JSON message nest at most 64 levels.
internal static class NestedParameters
{
    public static JsonObject Create(int levels)
    {
        JsonObject parameters = [];

        // Built from the innermost object outwards.
        for (int level = levels; level > 1; level--)
        {
            parameters = new JsonObject { ["level"] = parameters };
        }

        return parameters;
    }
}
