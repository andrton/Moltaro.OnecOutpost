using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// An agent runs only what its server signed (§3 and §7 of docs/protocol.md). The reader of a command verifies the
// signature over the exact bytes before it parses anything, so bytes without a valid signature are refused with
// `signature` whatever they hold. Verified bytes are refused by what they yield: `form` when they name no command,
// `protocol-version` and `malformed` under the identity of the command they name.
public sealed class CommandSignatureTests : IDisposable
{
    private readonly ECDsa _serverKey = Sample.NewKey();

    private readonly ECDsa _otherKey = Sample.NewKey();

    public static TheoryData<string> BytesThatNameNoCommand =>
    [
        "not a command",
        "[]",
        "{}",

        // The command of §7 without its `protocol`, and without its `id`.
        CommandSample.Text.Without("protocol").ToString(),
        CommandSample.Text.Without("id").ToString(),

        // A `protocol` that is no integer, and an `id` that is no identity in the form of §2.
        CommandSample.Text.With("protocol", "\"1\"").ToString(),
        CommandSample.Text.With("id", "\"9C1E2F3A-4B5C-4D6E-8F70-A1B2C3D4E5F6\"").ToString(),
        CommandSample.Text.With("id", "17").ToString(),
    ];

    public static TheoryData<string> CommandsOfAnotherVersion =>
    [
        // The command of §7 under another version; a command of a later version with members that this one does not
        // know; a version below the first.
        CommandSample.Text.With("protocol", "2").ToString(),
        "{\"protocol\":2,\"id\":\"9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6\",\"steps\":[{\"run\":true}]}",
        CommandSample.Text.With("protocol", "0").ToString(),
    ];

    public void Dispose()
    {
        _serverKey.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public void A_command_signed_with_the_pinned_key_is_read()
    {
        OutpostSignedCommand command = Signed(CommandSample.Text.ToBytes(), _serverKey);

        OutpostCommandMessage read = OutpostCodec.ReadCommand(_serverKey, command);

        Assert.Equal(Sample.Command, read.Id);
    }

    [Fact]
    public void A_command_whose_bytes_changed_in_any_place_is_refused_with_signature()
    {
        byte[] bytes = CommandSample.Text.ToBytes();
        byte[] signature = OutpostSignatures.SignCommand(_serverKey, bytes);

        for (int place = 0; place < bytes.Length; place++)
        {
            byte[] changed = [.. bytes];
            changed[place] ^= 1;

            ProtocolAssert.Refused(
                OutpostProtocolCodes.Signature,
                () => OutpostCodec.ReadCommand(_serverKey, new OutpostSignedCommand(changed, signature)));
        }
    }

    [Fact]
    public void A_command_with_bytes_added_or_cut_is_refused_with_signature()
    {
        byte[] bytes = CommandSample.Text.ToBytes();
        byte[] signature = OutpostSignatures.SignCommand(_serverKey, bytes);
        byte[] longer = [.. bytes, (byte)' '];
        byte[] shorter = bytes[..^1];

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadCommand(_serverKey, new OutpostSignedCommand(longer, signature)));
        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadCommand(_serverKey, new OutpostSignedCommand(shorter, signature)));
    }

    [Fact]
    public void A_command_signed_with_another_key_is_refused_with_signature()
    {
        OutpostSignedCommand command = Signed(CommandSample.Text.ToBytes(), _otherKey);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => OutpostCodec.ReadCommand(_serverKey, command));
    }

    // Had anything parsed these bytes, the refusal would be `form`.
    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    public void Bytes_that_do_not_parse_and_carry_no_valid_signature_are_refused_with_signature_never_with_form(
        int signatureLength)
    {
        byte[] bytes = MessageText.Utf8("not a command");

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadCommand(_serverKey, new OutpostSignedCommand(bytes, new byte[signatureLength])));
        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadCommand(_serverKey, Signed(bytes, _otherKey)));
    }

    [Theory]
    [MemberData(nameof(BytesThatNameNoCommand))]
    public void Verified_bytes_that_yield_no_protocol_or_no_id_are_refused_with_form_and_name_no_command(string text)
    {
        OutpostSignedCommand command = Signed(MessageText.Utf8(text), _serverKey);

        OutpostProtocolException refusal = ProtocolAssert.Refused(
            OutpostProtocolCodes.Form,
            () => OutpostCodec.ReadCommand(_serverKey, command));

        Assert.Null(refusal.CommandId);
    }

    [Theory]
    [MemberData(nameof(CommandsOfAnotherVersion))]
    public void A_verified_command_of_another_version_is_refused_with_protocol_version_under_its_identity(string text)
    {
        OutpostSignedCommand command = Signed(MessageText.Utf8(text), _serverKey);

        OutpostProtocolException refusal = ProtocolAssert.Refused(
            OutpostProtocolCodes.ProtocolVersion,
            () => OutpostCodec.ReadCommand(_serverKey, command));

        Assert.Equal(Sample.Command, refusal.CommandId);
    }

    [Fact]
    public void A_verified_command_of_this_version_that_breaks_the_rules_of_a_command_is_refused_with_malformed_under_its_identity()
    {
        OutpostSignedCommand command = Signed(CommandSample.Text.Without("script").ToBytes(), _serverKey);

        OutpostProtocolException refusal = ProtocolAssert.Refused(
            OutpostOutcomeCodes.Malformed,
            () => OutpostCodec.ReadCommand(_serverKey, command));

        Assert.Equal(Sample.Command, refusal.CommandId);
    }

    private static OutpostSignedCommand Signed(byte[] bytes, ECDsa key)
    {
        return new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(key, bytes));
    }
}
