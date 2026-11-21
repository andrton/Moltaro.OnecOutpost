using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Codec;

// The writers and the readers of the messages of docs/protocol.md. They perform no I/O and know nothing of HTTP or
// of the store; every rule they apply is a rule of that document.
//
// The readers verify first: every signed reader verifies the signature of §3 before it parses anything. It throws
// OutpostProtocolException with `signature` when the signature does not verify, and with `form` for a verified body
// that breaks §2 or the rules of its message. The checks of size belong to the callers: the server checks the length
// of the outcome member against the limit of its command, and the endpoints check the body limits of §10.
internal static class OutpostCodec
{
    // Writers, by §2: UTF-8 without a byte order mark, no white space between tokens, only the escapes of §2, times
    // and durations truncated to milliseconds. Each returns the exact bytes to sign and to send.

    // Writes the body of a `pair` request (§6.1).
    public static byte[] Write(OutpostPairRequest message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of the answer to `pair` (§6.1).
    public static byte[] Write(OutpostPairAnswer message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of a `poll` request (§6.2). A base without writing is written with `"write":null`.
    public static byte[] Write(OutpostPollRequest message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of the answer to `poll` (§6.2).
    public static byte[] Write(OutpostPollAnswer message)
    {
        throw new NotSupportedException();
    }

    // Writes the exact bytes of a command (§7), which the server signs and never writes again.
    public static byte[] Write(OutpostCommandMessage message)
    {
        throw new NotSupportedException();
    }

    // Writes an outcome object (§8), for the outcomes that an agent makes itself, such as `refused` or `unknown`. The
    // object of the executor is never written again: the agent forwards its bytes.
    public static byte[] Write(OutpostOutcomeMessage message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of an `outcome` request (§6.3), with the bytes of the outcome object as they are.
    public static byte[] Write(OutpostOutcomeRequest message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of the answer to `outcome` (§6.3).
    public static byte[] Write(OutpostOutcomeAnswer message)
    {
        throw new NotSupportedException();
    }

    // Writes the body of a signed refusal (§9).
    public static byte[] Write(OutpostRefusal message)
    {
        throw new NotSupportedException();
    }

    // Writes the one unsigned body of §9, `{"code":"protocol-version"}`, for a path that names a version the server
    // does not speak.
    public static byte[] WriteUnsupportedVersion()
    {
        throw new NotSupportedException();
    }

    // The size of a command (§7), which CommandSizeLimit bounds: the length of the text of the script between its
    // quotes plus the length of the text of the parameters, both as the writer of §2 writes them into the command, in
    // UTF-8 with every escape counted as it is written. Parameters without members are the 2 bytes `{}`. An escaped
    // control character counts 6 bytes, so the size is a long. The caller has refused U+0000 and lone surrogates
    // before: the rules of the script and of the parameters allow neither, and the size of a text that holds one
    // means nothing.
    public static long CommandSize(string script, JsonElement parameters)
    {
        return TextSize(script) + ValueSize(parameters);
    }

    // Readers of the server: they verify the request, then parse it; `pair` the other way round (§3).

    // Reads the body of a `pair` request (§6.1). The one reader of the server that parses first, because the key to
    // verify with is in the body: it reads the body by §2, then verifies the signature with the key it carries. It
    // throws `form` for a body that breaks §2 or §6.1, and `signature` when the signature does not verify. It does
    // not check the proof: that needs the secret of the token.
    public static OutpostPairRequest ReadPairRequest(Guid agent, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads the body of a `poll` request (§6.2), after it verified the signature with the agent's key. It throws
    // `signature`, then `form` for a body that breaks §2 or §6.2: the ranges and limits of its members, base keys
    // that repeat, entries of `received` that repeat, and a sum of `free` beyond the room left in `received`
    // included.
    public static OutpostPollRequest ReadPollRequest(
        ECDsa agentKey,
        Guid agent,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads the body of an `outcome` request (§6.3), after it verified the signature with the agent's key. It reads
    // the whole body by §2, the `outcome` member included (step 1 of §6.3), and keeps that member as a slice of
    // `body`, without a copy. It throws `signature`, then `form`. The check of the size and ReadOutcome follow it, in
    // the order of §6.3, and are the caller's.
    public static OutpostOutcomeRequest ReadOutcomeRequest(
        ECDsa agentKey,
        Guid agent,
        ReadOnlyMemory<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Readers of the agent: they verify the answer, then parse it; `pair` checks the fingerprint first (§3). Each
    // takes what it compares as parameters, and only hashes `requestBody`, the body of the request that the answer
    // answers (§9, "verified").

    // Reads the body of the answer `200` to `pair` (§6.1). The one reader of the agent that parses first, because the
    // server's key is in the answer. In this order:
    // - it throws `form` for a body from which no server key can be read;
    // - it checks the fingerprint of that key against `fingerprint`, the token's, and throws `server-key` on a
    //   mismatch, a code of the codec only, never sent;
    // - it verifies the signature with that key, and throws `signature`;
    // - it throws `form` for a body that breaks §2 or §6.1, and for an answer whose `agent` is not `agent` or whose
    //   `key` is not `sentKey`.
    // A refusal of `pair` is a bare 403 without a body, and has no reader.
    public static OutpostPairAnswer ReadPairAnswer(
        string fingerprint,
        Guid agent,
        ReadOnlySpan<byte> sentKey,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads the body of the answer `200` to `poll` (§6.2), after it verified the signature with the server's key. It
    // throws `signature`, then `form` for a body that breaks §2 or §6.2, for more commands than `totalFree`, the sum
    // of `free` in the poll, and for commands in an answer whose `newest` is not null. It verifies and reads no
    // command: ReadCommand does. The count by base and mode needs the commands checked, so the agent makes it.
    public static OutpostPollAnswer ReadPollAnswer(
        ECDsa serverKey,
        Guid agent,
        int totalFree,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads the body of the answer `200` to `outcome` (§6.3), after it verified the signature with the server's key.
    // It throws `signature`, then `form` for a body that breaks §2 or §6.3, and when `confirmed` is not `command`.
    public static OutpostOutcomeAnswer ReadOutcomeAnswer(
        ECDsa serverKey,
        Guid agent,
        Guid command,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads the body of a signed refusal (§9), after it verified the signature with the server's key. It throws
    // `signature`, then `form` for a body that breaks §2 or §9, and for a code that is not one of the signed refusals
    // of §9 under `status`.
    public static OutpostRefusal ReadRefusal(
        ECDsa serverKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Reads a command (§7). It verifies the signature over the bytes with the server's key and throws `signature`,
    // then reads `protocol` and `id`:
    // - verified bytes that do not yield both throw `form`, without a CommandId;
    // - a command of another version throws `protocol-version` with its CommandId, and the rest of its bytes are not
    //   read;
    // - a verified command of this version that breaks §7 throws `malformed` with its CommandId.
    // The other checks of step 4 of §7, which need the agent's identity, policy and clock, are the agent's.
    public static OutpostCommandMessage ReadCommand(ECDsa serverKey, OutpostSignedCommand command)
    {
        throw new NotSupportedException();
    }

    // Unsigned: the one unsigned body of §9, recognized for a reason and the console.

    // True when the body is exactly the body that WriteUnsupportedVersion writes, for reason 2 of §9 and the console.
    // It verifies nothing, and nothing is acted on because of it. It throws nothing.
    public static bool IsUnsupportedVersion(ReadOnlySpan<byte> body)
    {
        throw new NotSupportedException();
    }

    // Unsigned: the outcome object inside a verified request, or in a file of the outbox.

    // Reads an outcome object (§8). It applies §2 and the member rules of §8 for the mode, the outcomes that an agent
    // may send included, and throws `form`. A null mode, for a command the server does not know, accepts what either
    // mode allows. The agent checks the object of the executor with it for the mode of the command, and then with
    // OutpostOutcomeCodes.IsExecutorOutcome; nothing changes the object.
    public static OutpostOutcomeMessage ReadOutcome(OutpostModeEnum? mode, ReadOnlySpan<byte> outcome)
    {
        throw new NotSupportedException();
    }

    // The levels of §2 that a value nests, counted from the value itself: a scalar is 0, and an object or an array is
    // 1 plus the deepest of its members. Parameters and a result may nest OutpostProtocol.MaxValueDepth levels. It
    // throws nothing.
    public static int ValueDepth(JsonElement value)
    {
        int deepest = 0;

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty member in value.EnumerateObject())
                {
                    deepest = Math.Max(deepest, ValueDepth(member.Value));
                }

                return 1 + deepest;

            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                {
                    deepest = Math.Max(deepest, ValueDepth(item));
                }

                return 1 + deepest;

            default:
                return 0;
        }
    }

    // The bytes of a value as the writer of §2 writes it: no white space, and every string by TextSize.
    private static long ValueSize(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    // The braces, and a comma between two members.
                    long size = 2;
                    int count = 0;

                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        // The name in its quotes, its colon and the value.
                        size += 2 + TextSize(member.Name) + 1 + ValueSize(member.Value);
                        count++;
                    }

                    return size + Math.Max(count - 1, 0);
                }

            case JsonValueKind.Array:
                {
                    // The brackets, and a comma between two items.
                    long size = 2;
                    int count = 0;

                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        size += ValueSize(item);
                        count++;
                    }

                    return size + Math.Max(count - 1, 0);
                }

            case JsonValueKind.String:
                // The two quotes and the text.
                return 2 + TextSize(value.GetString()!);

            default:
                // A number, true, false or null: ASCII, counted as written.
                return value.GetRawText().Length;
        }
    }

    // The bytes of the text of a string between its quotes, as the writer of §2 writes it: UTF-8, with `"`, `\` and
    // the control characters that have a short escape as 2 bytes, every other control character as the 6 bytes of
    // `\u00XX`, and every other character as it is.
    private static long TextSize(string text)
    {
        long size = Encoding.UTF8.GetByteCount(text);

        foreach (char character in text)
        {
            if (character is '"' or '\\' or '\b' or '\t' or '\n' or '\f' or '\r')
            {
                // One byte in UTF-8, two as an escape.
                size += 1;
            }
            else if (character < ' ')
            {
                // One byte in UTF-8, six as an escape.
                size += 5;
            }
        }

        return size;
    }
}
