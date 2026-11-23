using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using static Moltaro.OnecOutpost.Protocol.Codec.OutpostJsonReader;

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
    // An agent version of §2 is 1 to 64 characters of this alphabet.
    private const int MaxAgentVersionLength = 64;

    // A signature of §2 is 64 bytes.
    private const int SignatureLength = 64;

    // The status of the answer that the readers of `pair`, `poll` and `outcome` read (§6).
    private const int OkStatus = 200;

    private static readonly SearchValues<char> AgentVersionCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-+");

    // Writers, by §2: UTF-8 without a byte order mark, no white space between tokens, only the escapes of §2, times
    // and durations truncated to milliseconds. Each returns the exact bytes to sign and to send.

    // Writes the body of a `pair` request (§6.1).
    public static byte[] Write(OutpostPairRequest message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("agentVersion");
        writer.String(message.AgentVersion);
        writer.Name("key");
        writer.Bytes(message.Key);
        writer.Name("proof");
        writer.Bytes(message.Proof);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of the answer to `pair` (§6.1).
    public static byte[] Write(OutpostPairAnswer message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("time");
        writer.Time(message.Time);
        writer.Name("agent");
        writer.Id(message.Agent);
        writer.Name("key");
        writer.Bytes(message.Key);
        writer.Name("serverKey");
        writer.Bytes(message.ServerKey);
        writer.Name("pollTimeoutMs");
        writer.Duration(message.PollTimeout);
        writer.Name("leaseGraceMs");
        writer.Duration(message.LeaseGrace);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of a `poll` request (§6.2). A base without writing is written with `"write":null`.
    public static byte[] Write(OutpostPollRequest message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("number");
        writer.Integer(message.Number);
        writer.Name("time");
        writer.Time(message.Time);
        writer.Name("agentVersion");
        writer.String(message.AgentVersion);
        writer.Name("pollTimeoutMs");
        writer.Duration(message.PollTimeout);

        writer.Name("bases");
        writer.StartArray();

        foreach (OutpostPollBase pollBase in message.Bases)
        {
            writer.StartObject();
            writer.Name("key");
            writer.String(pollBase.Key);
            writer.Name("read");
            WritePollMode(writer, pollBase.Read);
            writer.Name("write");
            WritePollMode(writer, pollBase.Write);
            writer.EndObject();
        }

        writer.EndArray();

        writer.Name("received");
        writer.StartArray();

        foreach (Guid command in message.Received)
        {
            writer.Id(command);
        }

        writer.EndArray();
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of the answer to `poll` (§6.2).
    public static byte[] Write(OutpostPollAnswer message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("time");
        writer.Time(message.Time);
        writer.Name("newest");
        writer.NullableInteger(message.Newest);
        writer.Name("pollTimeoutMs");
        writer.Duration(message.PollTimeout);
        writer.Name("leaseGraceMs");
        writer.Duration(message.LeaseGrace);

        writer.Name("bases");
        writer.StartObject();
        writer.Name("held");
        writer.Strings(message.Held);
        writer.Name("claimed");
        writer.Strings(message.Claimed);
        writer.EndObject();

        writer.Name("commands");
        writer.StartArray();

        foreach (OutpostSignedCommand command in message.Commands)
        {
            writer.StartObject();
            writer.Name("bytes");
            writer.Bytes(command.Bytes);
            writer.Name("signature");
            writer.Bytes(command.Signature);
            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the exact bytes of a command (§7), which the server signs and never writes again.
    public static byte[] Write(OutpostCommandMessage message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("protocol");
        writer.Integer(message.Protocol);
        writer.Name("id");
        writer.Id(message.Id);
        writer.Name("agent");
        writer.Id(message.Agent);
        writer.Name("base");
        writer.String(message.Base);
        writer.Name("mode");
        writer.String(OutpostForms.WriteMode(message.Mode));
        writer.Name("callerKey");
        writer.NullableString(message.CallerKey);

        // The two pieces that CommandSize counts, written by the very methods that it counts with.
        writer.Name("script");
        writer.String(message.Script);
        writer.Name("parameters");
        writer.Value(message.Parameters);

        writer.Name("issued");
        writer.Time(message.Issued);
        writer.Name("deadline");
        writer.Time(message.Deadline);
        writer.Name("skewMs");
        writer.Duration(message.Skew);
        writer.Name("outcomeLimit");
        writer.Integer(message.OutcomeLimit);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes an outcome object (§8), for the outcomes that an agent makes itself, such as `refused` or `unknown`. The
    // object of the executor is never written again: the agent forwards its bytes.
    public static byte[] Write(OutpostOutcomeMessage message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("kind");
        writer.String(OutpostForms.WriteOutcomeKind(message.Kind));
        writer.Name("code");
        writer.NullableString(message.Code);
        writer.Name("result");

        if (message.Result is JsonElement result)
        {
            writer.Value(result);
        }
        else
        {
            writer.Null();
        }

        writer.Name("errorText");
        writer.NullableString(message.ErrorText);
        writer.Name("messages");
        writer.Strings(message.Messages);
        writer.Name("messagesTrimmed");
        writer.Boolean(message.MessagesTrimmed);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of an `outcome` request (§6.3), with the bytes of the outcome object as they are.
    public static byte[] Write(OutpostOutcomeRequest message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("command");
        writer.Id(message.Command);
        writer.Name("outcome");
        writer.Raw(message.Outcome.Span);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of the answer to `outcome` (§6.3).
    public static byte[] Write(OutpostOutcomeAnswer message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("time");
        writer.Time(message.Time);
        writer.Name("confirmed");
        writer.Id(message.Confirmed);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the body of a signed refusal (§9).
    public static byte[] Write(OutpostRefusal message)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("time");
        writer.Time(message.Time);
        writer.Name("code");
        writer.String(message.Code);
        writer.EndObject();

        return writer.ToArray();
    }

    // Writes the one unsigned body of §9, `{"code":"protocol-version"}`, for a path that names a version the server
    // does not speak.
    public static byte[] WriteUnsupportedVersion()
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("code");
        writer.String(OutpostProtocolCodes.ProtocolVersion);
        writer.EndObject();

        return writer.ToArray();
    }

    // The size of a command (§7), which CommandSizeLimit bounds: the length of the text of the script between its
    // quotes plus the length of the text of the parameters, both as the writer of §2 writes them into the command, in
    // UTF-8 with every escape counted as it is written. Parameters without members are the 2 bytes `{}`. An escaped
    // control character counts 6 bytes, so the size is a long. The caller has refused U+0000 and lone surrogates
    // before: the rules of the script and of the parameters allow neither, and the size of a text that holds one
    // means nothing.
    public static long CommandSize(string script, JsonElement parameters)
    {
        // The methods of the writer that Write(OutpostCommandMessage) writes the two pieces with, counting instead of
        // keeping the bytes, so that the size and the bytes of a command can never disagree. Each piece has a counter
        // of its own, so that no comma comes between them.
        OutpostJsonWriter scriptText = OutpostJsonWriter.Counter();
        scriptText.String(script);

        OutpostJsonWriter parametersText = OutpostJsonWriter.Counter();
        parametersText.Value(parameters);

        // The two quotes of the script are not part of its text.
        return scriptText.Length - 2 + parametersText.Length;
    }

    // Readers of the server: they verify the request, then parse it; `pair` the other way round (§3).

    // Reads the body of a `pair` request (§6.1). The one reader of the server that parses first, because the key to
    // verify with is in the body: it reads the body by §2, then verifies the signature with the key it carries. It
    // throws `form` for a body that breaks §2 or §6.1, and `signature` when the signature does not verify. It does
    // not check the proof: that needs the secret of the token.
    public static OutpostPairRequest ReadPairRequest(Guid agent, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signature)
    {
        using JsonDocument document = ParseMessage(body.ToArray());
        JsonElement request = Members(document.RootElement, "agentVersion", "key", "proof");

        string agentVersion = ReadAgentVersion(request);
        byte[] key = ReadBytes(request, "key");
        byte[] proof = ReadBytes(request, "proof");

        // The pairing proof of §3 is an HMAC-SHA256.
        if (proof.Length != HMACSHA256.HashSizeInBytes)
        {
            throw Form("The proof is not 32 bytes.");
        }

        using ECDsa agentKey = OutpostKeys.ImportPublicKey(key);

        if (!OutpostSignatures.VerifyRequest(agentKey, OutpostRequestKindEnum.Pair, agent, body, signature))
        {
            throw NotVerified("The pairing request is not signed with the key it carries, for this agent.");
        }

        return new OutpostPairRequest(agentVersion, key, proof);
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
        if (!OutpostSignatures.VerifyRequest(agentKey, OutpostRequestKindEnum.Poll, agent, body, signature))
        {
            throw NotVerified("The poll is not signed with the key of its agent.");
        }

        using JsonDocument document = ParseMessage(body.ToArray());
        JsonElement poll = Members(
            document.RootElement,
            "number",
            "time",
            "agentVersion",
            "pollTimeoutMs",
            "bases",
            "received");

        long number = ReadInteger(poll, "number", 1, OutpostProtocol.MaxPollNumber);
        DateTimeOffset time = ReadTime(poll, "time");
        string agentVersion = ReadAgentVersion(poll);
        TimeSpan pollTimeout = ReadDuration(
            poll,
            "pollTimeoutMs",
            OutpostProtocol.MinPollTimeout,
            OutpostProtocol.MaxPollTimeout);
        List<OutpostPollBase> bases = ReadPollBases(poll);
        List<Guid> received = ReadIds(poll, "received");

        if (received.Count > OutpostProtocol.MaxReceived)
        {
            throw Form($"The list of received commands holds more than {OutpostProtocol.MaxReceived} entries.");
        }

        if (received.Distinct().Count() != received.Count)
        {
            throw Form("A command is listed twice among the received commands.");
        }

        // The sum of `free` over all modes is at most the room left in the list, so that no answer can overfill it.
        int free = bases.Sum(pollBase => pollBase.Read.Free + (pollBase.Write?.Free ?? 0));

        if (free > OutpostProtocol.MaxReceived - received.Count)
        {
            throw Form("The free workers of the poll are more than the room left in the list of received commands.");
        }

        return new OutpostPollRequest(number, time, agentVersion, pollTimeout, bases, received);
    }

    // Reads the body of an `outcome` request (§6.3), after it verified the signature with the agent's key. It reads
    // the whole body by §2, the `outcome` member included (step 1 of §6.3), and keeps that member as a slice of
    // `body`, without a copy. It throws `signature`, then `form`.
    //
    // It gives the document of the body that it read by §2, which reads `body` in place, so that ReadRequestOutcome
    // applies §8 to the `outcome` member once its size is checked, without reading it again. The caller disposes the
    // document; it is disposed already when the reader throws.
    public static OutpostOutcomeRequest ReadOutcomeRequest(
        ECDsa agentKey,
        Guid agent,
        ReadOnlyMemory<byte> body,
        ReadOnlySpan<byte> signature,
        out JsonDocument document)
    {
        if (!OutpostSignatures.VerifyRequest(agentKey, OutpostRequestKindEnum.Outcome, agent, body.Span, signature))
        {
            throw NotVerified("The outcome is not signed with the key of its agent.");
        }

        document = ParseMessage(body);

        try
        {
            JsonElement request = Members(document.RootElement, "command", "outcome");

            Guid command = ReadId(request, "command");
            JsonElement outcome = request.GetProperty("outcome");

            if (outcome.ValueKind != JsonValueKind.Object)
            {
                throw Form("The outcome is not an object.");
            }

            // The document reads the body in place, so the raw bytes of the member lie inside the body, and their
            // offset gives the slice: the size of the outcome is the length of the bytes that came.
            ReadOnlySpan<byte> outcomeBytes = JsonMarshal.GetRawUtf8Value(outcome);
            body.Span.Overlaps(outcomeBytes, out int offset);

            return new OutpostOutcomeRequest(command, body.Slice(offset, outcomeBytes.Length));
        }
        catch
        {
            document.Dispose();
            throw;
        }
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
        using JsonDocument document = ParseJson(body.ToArray());
        JsonElement root = document.RootElement;

        // Only the server key is looked at before the body is verified with it.
        if (!TryGetMember(root, "serverKey", out JsonElement serverKeyValue)
            || !TryReadBytes(serverKeyValue, out byte[]? serverKeyBytes))
        {
            throw Form("The answer holds no server key that can be read.");
        }

        using ECDsa serverKey = OutpostKeys.ImportPublicKey(serverKeyBytes);

        if (OutpostKeys.Fingerprint(serverKeyBytes) != fingerprint)
        {
            throw new OutpostProtocolException(
                OutpostProtocolCodes.ServerKey,
                "The server key of the answer does not match the fingerprint of the token.");
        }

        bool verified = OutpostSignatures.VerifyAnswer(
            serverKey,
            OutpostRequestKindEnum.Pair,
            agent,
            OkStatus,
            requestBody,
            body,
            signature);

        if (!verified)
        {
            throw NotVerified("The answer is not signed with the server key it carries, for this request.");
        }

        CheckText(root);
        JsonElement answer = Members(root, "time", "agent", "key", "serverKey", "pollTimeoutMs", "leaseGraceMs");

        DateTimeOffset time = ReadTime(answer, "time");
        Guid answeredAgent = ReadId(answer, "agent");
        byte[] key = ReadBytes(answer, "key");
        TimeSpan pollTimeout = ReadDuration(
            answer,
            "pollTimeoutMs",
            OutpostProtocol.MinPollTimeout,
            OutpostProtocol.MaxPollTimeout);
        TimeSpan leaseGrace = ReadDuration(
            answer,
            "leaseGraceMs",
            OutpostProtocol.MinLeaseGrace,
            OutpostProtocol.MaxLeaseGrace);

        if (answeredAgent != agent || !key.AsSpan().SequenceEqual(sentKey))
        {
            throw Form("The answer pairs another agent or another key than the request sent.");
        }

        return new OutpostPairAnswer(time, answeredAgent, key, serverKeyBytes, pollTimeout, leaseGrace);
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
        bool verified = OutpostSignatures.VerifyAnswer(
            serverKey,
            OutpostRequestKindEnum.Poll,
            agent,
            OkStatus,
            requestBody,
            body,
            signature);

        if (!verified)
        {
            throw NotVerified("The answer to the poll is not signed with the server key, for this poll.");
        }

        using JsonDocument document = ParseMessage(body.ToArray());
        JsonElement answer = Members(
            document.RootElement,
            "time",
            "newest",
            "pollTimeoutMs",
            "leaseGraceMs",
            "bases",
            "commands");

        DateTimeOffset time = ReadTime(answer, "time");
        long? newest = ReadNullableInteger(answer, "newest", 1, OutpostProtocol.MaxPollNumber);
        TimeSpan pollTimeout = ReadDuration(
            answer,
            "pollTimeoutMs",
            OutpostProtocol.MinPollTimeout,
            OutpostProtocol.MaxPollTimeout);
        TimeSpan leaseGrace = ReadDuration(
            answer,
            "leaseGraceMs",
            OutpostProtocol.MinLeaseGrace,
            OutpostProtocol.MaxLeaseGrace);

        JsonElement bases = Members(answer.GetProperty("bases"), "held", "claimed");
        List<string> held = ReadBaseKeys(bases, "held");
        List<string> claimed = ReadBaseKeys(bases, "claimed");

        List<OutpostSignedCommand> commands = [];

        foreach (JsonElement item in ReadArray(answer, "commands").EnumerateArray())
        {
            JsonElement command = Members(item, "bytes", "signature");
            byte[] bytes = ReadBytes(command, "bytes");
            byte[] commandSignature = ReadBytes(command, "signature");

            if (commandSignature.Length != SignatureLength)
            {
                throw Form("The signature of a command is not 64 bytes.");
            }

            commands.Add(new OutpostSignedCommand(bytes, commandSignature));
        }

        if (commands.Count > totalFree)
        {
            throw Form("The answer holds more commands than the poll had free workers.");
        }

        if (newest is not null && commands.Count > 0)
        {
            throw Form("The answer to an older poll holds commands.");
        }

        return new OutpostPollAnswer(time, newest, pollTimeout, leaseGrace, held, claimed, commands);
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
        bool verified = OutpostSignatures.VerifyAnswer(
            serverKey,
            OutpostRequestKindEnum.Outcome,
            agent,
            OkStatus,
            requestBody,
            body,
            signature);

        if (!verified)
        {
            throw NotVerified("The answer to the outcome is not signed with the server key, for this outcome.");
        }

        using JsonDocument document = ParseMessage(body.ToArray());
        JsonElement answer = Members(document.RootElement, "time", "confirmed");

        DateTimeOffset time = ReadTime(answer, "time");
        Guid confirmed = ReadId(answer, "confirmed");

        if (confirmed != command)
        {
            throw Form("The answer confirms another command than the request sent.");
        }

        return new OutpostOutcomeAnswer(time, confirmed);
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
        if (!OutpostSignatures.VerifyAnswer(serverKey, kind, agent, status, requestBody, body, signature))
        {
            throw NotVerified("The refusal is not signed with the server key, for this request and status.");
        }

        using JsonDocument document = ParseMessage(body.ToArray());
        JsonElement refusal = Members(document.RootElement, "time", "code");

        DateTimeOffset time = ReadTime(refusal, "time");
        string code = ReadString(refusal, "code");

        // The table of signed refusals of §9.
        bool isSignedRefusal = (status, code) switch
        {
            (400, OutpostProtocolCodes.Form) => true,
            (401, OutpostProtocolCodes.Signature) => true,
            (413, OutpostProtocolCodes.ResultTooLarge) => true,
            _ => false,
        };

        if (!isSignedRefusal)
        {
            throw Form("The code of the refusal is not the one that the table of signed refusals gives its status.");
        }

        return new OutpostRefusal(time, code);
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
        if (!OutpostSignatures.VerifyCommand(serverKey, command.Bytes, command.Signature))
        {
            throw NotVerified("The command is not signed with the pinned server key.");
        }

        using JsonDocument document = ParseJson(command.Bytes);
        JsonElement root = document.RootElement;

        // Every version keeps `protocol` and `id` in this form, so they are read before anything else of the bytes.
        if (!TryGetMember(root, "protocol", out JsonElement protocolValue)
            || !TryReadInteger(protocolValue, out long protocol)
            || !TryGetMember(root, "id", out JsonElement idValue)
            || !TryReadId(idValue, out Guid id))
        {
            throw Form("The verified bytes yield no protocol and identity of a command.");
        }

        if (protocol != OutpostProtocol.Version)
        {
            throw new OutpostProtocolException(
                OutpostProtocolCodes.ProtocolVersion,
                $"The command is of protocol {protocol}, and this build speaks protocol {OutpostProtocol.Version}.",
                id);
        }

        try
        {
            CheckText(root);

            return ReadCommandMembers(root, id);
        }
        catch (OutpostProtocolException refusal) when (refusal.Code == OutpostProtocolCodes.Form)
        {
            // A verified command of this version that breaks §2 or §7 names itself: it is refused under its identity.
            throw new OutpostProtocolException(OutpostOutcomeCodes.Malformed, refusal.Message, id);
        }
    }

    // Unsigned: the one unsigned body of §9, recognized for a reason and the console.

    // True when the body is exactly the body that WriteUnsupportedVersion writes, for reason 2 of §9 and the console.
    // It verifies nothing, and nothing is acted on because of it. It throws nothing.
    public static bool IsUnsupportedVersion(ReadOnlySpan<byte> body)
    {
        return body.SequenceEqual(WriteUnsupportedVersion());
    }

    // Unsigned: the outcome object inside a verified request, or in a file of the outbox.

    // Reads an outcome object (§8). It applies §2 and the member rules of §8 for the mode, the outcomes that an agent
    // may send included, and throws `form`. A null mode, for a command the server does not know, accepts what either
    // mode allows. The agent checks the object that a COM worker hands over with it for the mode of the command, and
    // then with OutpostOutcomeCodes.IsComWorkerOutcome; nothing changes the object.
    public static OutpostOutcomeMessage ReadOutcome(OutpostModeEnum? mode, ReadOnlySpan<byte> outcome)
    {
        using JsonDocument document = ParseMessage(outcome.ToArray());
        OutpostOutcomeMessage message = ReadOutcomeMembers(mode, document.RootElement);

        // The document is disposed when the reader returns, so the result is a copy of its own.
        return message with { Result = message.Result?.Clone() };
    }

    // Reads the outcome object of a request that ReadOutcomeRequest read into `request` and checked by §2, the server's
    // step 3 of §6.3: the member rules of §8 for the mode, as ReadOutcome applies them, without a second check of §2
    // and without a copy. The result of the message is an element of `request`, valid until the caller disposes it.
    public static OutpostOutcomeMessage ReadRequestOutcome(OutpostModeEnum? mode, JsonDocument request)
    {
        return ReadOutcomeMembers(mode, request.RootElement.GetProperty("outcome"));
    }

    // The members of an outcome object by §8 for the mode, in an object that §2 has been checked on.
    private static OutpostOutcomeMessage ReadOutcomeMembers(OutpostModeEnum? mode, JsonElement outcome)
    {
        JsonElement message = Members(
            outcome,
            "kind",
            "code",
            "result",
            "errorText",
            "messages",
            "messagesTrimmed");

        OutpostAgentOutcomeKindEnum kind = ReadKind(message);
        string? code = ReadNullableString(message, "code");

        bool maySend = mode is null
            ? OutpostOutcomeCodes.IsAgentOutcome(OutpostModeEnum.Read, kind, code)
                || OutpostOutcomeCodes.IsAgentOutcome(OutpostModeEnum.Write, kind, code)
            : OutpostOutcomeCodes.IsAgentOutcome(mode.Value, kind, code);

        if (!maySend)
        {
            throw Form("An agent may not send an outcome of this kind and code for the mode of its command.");
        }

        JsonElement? result = ReadResult(message, kind);
        string? errorText = ReadNullableString(message, "errorText");
        List<string> messages = ReadStrings(message, "messages");
        bool messagesTrimmed = ReadBoolean(message, "messagesTrimmed");

        // The text of the error that 1C raised belongs to `failed` with `script-error`, and to nothing else.
        bool carriesErrorText = kind == OutpostAgentOutcomeKindEnum.Failed && code == OutpostOutcomeCodes.ScriptError;

        if (carriesErrorText != (errorText is not null))
        {
            throw Form("The error text is a string for a script error, and null for every other outcome.");
        }

        // A command refused or unknown carries no messages of 1C.
        if (kind is OutpostAgentOutcomeKindEnum.Refused or OutpostAgentOutcomeKindEnum.Unknown
            && (messages.Count > 0 || messagesTrimmed))
        {
            throw Form("An outcome that is refused or unknown has no messages, and none trimmed.");
        }

        return new OutpostOutcomeMessage(kind, code, result, errorText, messages, messagesTrimmed);
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

    // The members of a command of this version after `protocol` and `id` (§7), whose text CheckText has checked.
    private static OutpostCommandMessage ReadCommandMembers(JsonElement root, Guid id)
    {
        JsonElement command = Members(
            root,
            "protocol",
            "id",
            "agent",
            "base",
            "mode",
            "callerKey",
            "script",
            "parameters",
            "issued",
            "deadline",
            "skewMs",
            "outcomeLimit");

        Guid agent = ReadId(command, "agent");
        string baseKey = ReadBaseKey(command, "base");
        OutpostModeEnum mode = ReadMode(command);
        string? callerKey = ReadNullableString(command, "callerKey");

        // A Write has a caller key; a Read may have one.
        if (callerKey is null ? mode == OutpostModeEnum.Write : !OutpostNames.IsCallerKey(callerKey))
        {
            throw Form("The caller key is not a caller key by the rule of names, or a Write has none.");
        }

        string script = ReadString(command, "script");

        // CheckText has refused U+0000 and lone surrogates in every string, so what is left of the rule of scripts is
        // IsScript.
        if (!OutpostNames.IsScript(script))
        {
            throw Form("The script breaks the rule of scripts.");
        }

        JsonElement parameters = command.GetProperty("parameters");

        if (parameters.ValueKind != JsonValueKind.Object || ValueDepth(parameters) > OutpostProtocol.MaxValueDepth)
        {
            throw Form($"The parameters are not an object of at most {OutpostProtocol.MaxValueDepth} levels.");
        }

        DateTimeOffset issued = ReadTime(command, "issued");
        DateTimeOffset deadline = ReadTime(command, "deadline");
        TimeSpan takeTimeout = deadline - issued;

        if (takeTimeout < OutpostProtocol.MinTakeTimeout || takeTimeout > OutpostProtocol.MaxTakeTimeout)
        {
            throw Form("The deadline is not after the time of issue by a duration in the range of TakeTimeout.");
        }

        TimeSpan skew = ReadDuration(command, "skewMs", OutpostProtocol.MinClockSkew, OutpostProtocol.MaxClockSkew);
        long outcomeLimit = ReadInteger(
            command,
            "outcomeLimit",
            OutpostProtocol.MinOutcomeSizeLimit,
            OutpostProtocol.MaxOutcomeSizeLimit);

        // The document is disposed when the reader returns, so the parameters are a copy of their own.
        return new OutpostCommandMessage(
            OutpostProtocol.Version,
            id,
            agent,
            baseKey,
            mode,
            callerKey,
            script,
            parameters.Clone(),
            issued,
            deadline,
            skew,
            (int)outcomeLimit);
    }

    // `bases` of a poll (§6.2): at most 256, with keys by the rule of names, each once. `read` is an object, and
    // `write` an object or null, null when the local policy does not open writing.
    private static List<OutpostPollBase> ReadPollBases(JsonElement poll)
    {
        List<OutpostPollBase> bases = [];
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (JsonElement item in ReadArray(poll, "bases").EnumerateArray())
        {
            JsonElement pollBase = Members(item, "key", "read", "write");
            string key = ReadBaseKey(pollBase, "key");

            if (!keys.Add(key))
            {
                throw Form("A base key appears twice among the bases of the poll.");
            }

            OutpostPollMode read = ReadPollMode(pollBase.GetProperty("read"));
            JsonElement writeValue = pollBase.GetProperty("write");
            OutpostPollMode? write = writeValue.ValueKind == JsonValueKind.Null ? null : ReadPollMode(writeValue);

            bases.Add(new OutpostPollBase(key, read, write));
        }

        if (bases.Count > OutpostProtocol.MaxBases)
        {
            throw Form($"The poll declares more than {OutpostProtocol.MaxBases} bases.");
        }

        return bases;
    }

    private static OutpostPollMode ReadPollMode(JsonElement value)
    {
        JsonElement mode = Members(value, "free", "runLimitMs");

        long free = ReadInteger(mode, "free", 0, OutpostProtocol.MaxWorkers);
        TimeSpan runLimit = ReadDuration(mode, "runLimitMs", OutpostProtocol.MinRunLimit, OutpostProtocol.MaxRunLimit);

        return new OutpostPollMode((int)free, runLimit);
    }

    private static void WritePollMode(OutpostJsonWriter writer, OutpostPollMode? mode)
    {
        if (mode is null)
        {
            writer.Null();

            return;
        }

        writer.StartObject();
        writer.Name("free");
        writer.Integer(mode.Free);
        writer.Name("runLimitMs");
        writer.Duration(mode.RunLimit);
        writer.EndObject();
    }

    // An agent version of §2: 1 to 64 characters, each one of A-Z a-z 0-9 . - +.
    private static string ReadAgentVersion(JsonElement message)
    {
        string version = ReadString(message, "agentVersion");

        bool isAgentVersion = version.Length is > 0 and <= MaxAgentVersionLength
            && !version.AsSpan().ContainsAnyExcept(AgentVersionCharacters);

        if (!isAgentVersion)
        {
            throw Form("The agent version is not 1 to 64 characters, each one of A-Z a-z 0-9 . - +.");
        }

        return version;
    }

    private static string ReadBaseKey(JsonElement message, string name)
    {
        string key = ReadString(message, name);

        if (!OutpostNames.IsBaseKey(key))
        {
            throw Form($"The member {name} is not a base key by the rule of names.");
        }

        return key;
    }

    private static List<string> ReadBaseKeys(JsonElement message, string name)
    {
        List<string> keys = ReadStrings(message, name);

        if (!keys.TrueForAll(OutpostNames.IsBaseKey))
        {
            throw Form($"An item of {name} is not a base key by the rule of names.");
        }

        return keys;
    }

    // `result` (§8): for `succeeded`, any value of at most 64 levels, `null` included; `null` for every other kind. The
    // value is an element of the document of the message.
    private static JsonElement? ReadResult(JsonElement message, OutpostAgentOutcomeKindEnum kind)
    {
        JsonElement result = message.GetProperty("result");

        if (kind != OutpostAgentOutcomeKindEnum.Succeeded)
        {
            if (result.ValueKind != JsonValueKind.Null)
            {
                throw Form("An outcome that did not succeed has a result.");
            }

            return null;
        }

        if (ValueDepth(result) > OutpostProtocol.MaxValueDepth)
        {
            throw Form($"The result nests deeper than {OutpostProtocol.MaxValueDepth} levels.");
        }

        return result;
    }

    private static OutpostModeEnum ReadMode(JsonElement command)
    {
        return OutpostForms.ReadMode(ReadString(command, "mode")) ?? throw Form("The mode is not read or write.");
    }

    // Only the server sets Expired and Unavailable, so an outcome object never holds them.
    private static OutpostAgentOutcomeKindEnum ReadKind(JsonElement message)
    {
        return OutpostForms.ReadOutcomeKind(ReadString(message, "kind"))
            ?? throw Form("The kind is not succeeded, failed, refused or unknown.");
    }

    private static OutpostProtocolException NotVerified(string reason)
    {
        return new OutpostProtocolException(OutpostProtocolCodes.Signature, reason);
    }
}
