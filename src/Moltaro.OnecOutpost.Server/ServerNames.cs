namespace Moltaro.OnecOutpost.Server;

// The rules of the names that only the server checks: the schema of the store, the prefix of the endpoints and the
// prefix of the keys of the signals. The names the agent checks as well are in OutpostNames of the protocol. Each Check
// method throws ArgumentNullException for null and ArgumentException for a value that breaks its rule, both with the
// ParamName it is given.
internal static class ServerNames
{
    private const int SchemaMaxLength = 63;

    // At most 64 of the 128 characters of a name of Moltaro.Signals: the server appends the identity of an agent or of a
    // command to the prefix.
    private const int SignalKeyPrefixMaxLength = 64;

    internal static void CheckSchema(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsSchema(value))
        {
            throw new ArgumentException(
                $"A schema is 1 to {SchemaMaxLength} characters, each one of a-z 0-9 _, the first a letter.",
                paramName);
        }
    }

    internal static void CheckEndpointPrefix(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsEndpointPrefix(value))
        {
            throw new ArgumentException(
                "An endpoint prefix is / followed by one or more segments of A-Z a-z 0-9 - _ . separated by /, not ending with /; no segment is . or ..",
                paramName);
        }
    }

    internal static void CheckSignalKeyPrefix(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsSignalKeyPrefix(value))
        {
            throw new ArgumentException(
                $"A signal key prefix is a name of Moltaro.Signals of 1 to {SignalKeyPrefixMaxLength} characters, each one of A-Z a-z 0-9 . _ : -, and no segment between the dots is empty.",
                paramName);
        }
    }

    // True for 1 to 63 characters, each one of a-z 0-9 _, the first a letter.
    private static bool IsSchema(string value)
    {
        if (value.Length is 0 or > SchemaMaxLength || !char.IsAsciiLetterLower(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    // True for / followed by one or more segments of A-Z a-z 0-9 - _ . separated by /, none of them . or ..; an empty
    // segment would mean // or a / at the end.
    private static bool IsEndpointPrefix(string value)
    {
        if (value.Length < 2 || value[0] != '/')
        {
            return false;
        }

        foreach (string segment in value[1..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }

            foreach (char character in segment)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // The rule of a name of Moltaro.Signals, as its ISignals states it, with at most 64 characters: each one of
    // A-Z a-z 0-9 . _ : -, where '.' separates segments and no segment is empty.
    private static bool IsSignalKeyPrefix(string value)
    {
        if (value.Length is 0 or > SignalKeyPrefixMaxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or ':' or '-'))
            {
                return false;
            }
        }

        // A '.' at either end, or two in a row, would leave a segment empty.
        return value[0] != '.' && value[^1] != '.' && !value.Contains("..", StringComparison.Ordinal);
    }
}
