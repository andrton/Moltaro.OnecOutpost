namespace Moltaro.OnecOutpost.Protocol;

// The rules of the names that both the server and the agent check, so that each rule lives once. A Check method
// throws ArgumentNullException for null and ArgumentException for a value that breaks its rule, both with the
// ParamName it is given. Lengths are counted in UTF-16 code units, as string.Length counts them.
internal static class OutpostNames
{
    private const int BaseKeyMaxLength = 64;

    private const int CallerKeyMaxLength = 200;

    private const int AgentNameMaxLength = 100;

    public static void CheckBaseKey(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsBaseKey(value))
        {
            throw new ArgumentException(
                $"A base key is 1 to {BaseKeyMaxLength} characters, each one of a-z 0-9 - _ ., the first a letter or a digit.",
                paramName);
        }
    }

    public static void CheckCallerKey(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Length is 0 or > CallerKeyMaxLength || value.AsSpan().ContainsAnyExceptInRange('!', '~'))
        {
            throw new ArgumentException(
                $"A caller key is 1 to {CallerKeyMaxLength} characters, each from U+0021 to U+007E: printable ASCII without the space.",
                paramName);
        }
    }

    public static void CheckAgentName(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsAgentName(value))
        {
            throw new ArgumentException(
                $"An agent name is 1 to {AgentNameMaxLength} characters, with no control character and no white space at the start or the end.",
                paramName);
        }

        if (HasLoneSurrogate(value))
        {
            throw new ArgumentException(
                "An agent name may not hold a lone surrogate, a code unit from U+D800 to U+DFFF without its pair, which UTF-8 cannot carry.",
                paramName);
        }
    }

    public static void CheckScript(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
        {
            throw new ArgumentException(
                "A script has at least one character that is not white space, and no U+0000.",
                paramName);
        }

        if (HasLoneSurrogate(value))
        {
            throw new ArgumentException(
                "A script may not hold a lone surrogate, a code unit from U+D800 to U+DFFF without its pair, which UTF-8 cannot carry.",
                paramName);
        }
    }

    // True when the text holds a lone surrogate: a UTF-16 code unit from U+D800 to U+DFFF without its pair, which no
    // UTF-8 can carry (Q6). The server checks the strings of the parameters of a command with it as well.
    public static bool HasLoneSurrogate(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsSurrogatePair(value, index))
            {
                // The low half is part of the pair, so it is skipped with it.
                index++;
            }
            else if (char.IsSurrogate(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    // True for 1 to 64 characters, each one of a-z 0-9 - _ ., the first a letter or a digit.
    public static bool IsBaseKey(string value)
    {
        if (value.Length is 0 or > BaseKeyMaxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAgentName(string value)
    {
        if (value.Length is 0 or > AgentNameMaxLength)
        {
            return false;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
