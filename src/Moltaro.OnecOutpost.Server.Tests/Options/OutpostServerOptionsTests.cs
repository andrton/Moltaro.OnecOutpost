using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Options;

// The rules of the options, which AddOutpostServer checks. The rules of the schema and of the prefix of the signal keys
// are stated here in full; the migrations apply the same rule of schemas, and their tests show only that they do.
public sealed class OutpostServerOptionsTests
{
    public static TheoryData<string, TimeSpan, TimeSpan> TimeRanges => new()
    {
        { nameof(OutpostServerOptions.PollTimeout), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5) },
        { nameof(OutpostServerOptions.LeaseGrace), TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5) },
        { nameof(OutpostServerOptions.TakeTimeout), TimeSpan.FromSeconds(1), TimeSpan.FromHours(1) },
        { nameof(OutpostServerOptions.ClockSkew), TimeSpan.FromSeconds(1), TimeSpan.FromHours(1) },
        { nameof(OutpostServerOptions.OutcomeRetention), TimeSpan.FromHours(1), TimeSpan.FromDays(365) },
        { nameof(OutpostServerOptions.PairingTokenLifetime), TimeSpan.FromMinutes(1), TimeSpan.FromDays(1) },
        { nameof(OutpostServerOptions.PurgeInterval), TimeSpan.FromMinutes(1), TimeSpan.FromDays(1) },
    };

    public static TheoryData<string, int, int> SizeRanges => new()
    {
        { nameof(OutpostServerOptions.CommandSizeLimit), 4 * 1024, 16 * 1024 * 1024 },
        { nameof(OutpostServerOptions.OutcomeSizeLimit), 64 * 1024, 64 * 1024 * 1024 },
    };

    public static TheoryData<string> GoodSchemas =>
    [
        "s",
        new string('s', 63),
        "outpost_2",
    ];

    public static TheoryData<string> BrokenSchemas =>
    [
        "",
        new string('s', 64),
        "Outpost",
        "2outpost",
        "_outpost",
        "out-post",
        "caf\u00e9",
    ];

    public static TheoryData<string> GoodSignalKeyPrefixes =>
    [
        "p",
        new string('p', 64),
        "Outpost.App_2:nodes-a",
    ];

    public static TheoryData<string> BrokenSignalKeyPrefixes =>
    [
        "",
        new string('p', 65),
        "out post",
        "outpost/agents",
        ".outpost",
        "outpost.",
        "outpost..agents",
        "caf\u00e9",
    ];

    [Fact]
    public void New_options_have_the_defaults_of_the_contract()
    {
        OutpostServerOptions options = new();

        Assert.Null(options.ConnectionString);
        Assert.Equal("outpost", options.Schema);
        Assert.Null(options.SigningKeyPem);
        Assert.Equal("outpost", options.SignalKeyPrefix);
        Assert.Equal(TimeSpan.FromSeconds(25), options.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LeaseGrace);
        Assert.Equal(TimeSpan.FromSeconds(30), options.TakeTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), options.ClockSkew);
        Assert.Equal(1024 * 1024, options.CommandSizeLimit);
        Assert.Equal(16 * 1024 * 1024, options.OutcomeSizeLimit);
        Assert.Equal(TimeSpan.FromDays(7), options.OutcomeRetention);
        Assert.Equal(TimeSpan.FromMinutes(15), options.PairingTokenLifetime);
        Assert.Equal(TimeSpan.FromHours(1), options.PurgeInterval);
        Assert.Same(TimeProvider.System, options.TimeProvider);
    }

    [Fact]
    public void The_defaults_with_a_connection_string_and_a_signing_key_are_accepted()
    {
        AddServer(_ => { });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void A_connection_string_that_is_null_empty_or_white_space_is_refused(string? connectionString)
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.ConnectionString),
            () => AddServer(options => options.ConnectionString = connectionString));
    }

    [Fact]
    public void The_signing_key_is_required()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.SigningKeyPem),
            () => AddServer(options => options.SigningKeyPem = null));
    }

    [Fact]
    public void The_time_provider_is_required()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.TimeProvider),
            () => AddServer(options => options.TimeProvider = null!));
    }

    [Theory]
    [MemberData(nameof(TimeRanges))]
    public void A_time_at_either_end_of_its_range_is_accepted(string property, TimeSpan minimum, TimeSpan maximum)
    {
        AddServer(options => SetTime(options, property, minimum));
        AddServer(options => SetTime(options, property, maximum));
    }

    [Theory]
    [MemberData(nameof(TimeRanges))]
    public void A_time_one_tick_outside_its_range_is_refused_with_the_name_of_its_property(
        string property,
        TimeSpan minimum,
        TimeSpan maximum)
    {
        TimeSpan tick = TimeSpan.FromTicks(1);

        Assert.Throws<ArgumentOutOfRangeException>(property, () => AddServer(options => SetTime(options, property, minimum - tick)));
        Assert.Throws<ArgumentOutOfRangeException>(property, () => AddServer(options => SetTime(options, property, maximum + tick)));
    }

    [Theory]
    [MemberData(nameof(SizeRanges))]
    public void A_size_at_either_end_of_its_range_is_accepted(string property, int minimum, int maximum)
    {
        AddServer(options => SetSize(options, property, minimum));
        AddServer(options => SetSize(options, property, maximum));
    }

    [Theory]
    [MemberData(nameof(SizeRanges))]
    public void A_size_one_byte_outside_its_range_is_refused_with_the_name_of_its_property(
        string property,
        int minimum,
        int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(property, () => AddServer(options => SetSize(options, property, minimum - 1)));
        Assert.Throws<ArgumentOutOfRangeException>(property, () => AddServer(options => SetSize(options, property, maximum + 1)));
    }

    [Theory]
    [MemberData(nameof(GoodSchemas))]
    public void A_schema_of_1_to_63_characters_of_a_z_0_9_and_underscore_starting_with_a_letter_is_accepted(string schema)
    {
        AddServer(options => options.Schema = schema);
    }

    [Theory]
    [MemberData(nameof(BrokenSchemas))]
    public void Any_other_schema_is_refused_with_the_name_of_its_property(string schema)
    {
        Assert.Throws<ArgumentException>(nameof(OutpostServerOptions.Schema), () => AddServer(options => options.Schema = schema));
    }

    [Fact]
    public void A_null_schema_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(nameof(OutpostServerOptions.Schema), () => AddServer(options => options.Schema = null!));
    }

    [Theory]
    [MemberData(nameof(GoodSignalKeyPrefixes))]
    public void A_signal_key_prefix_that_is_a_name_of_Moltaro_Signals_of_at_most_64_characters_is_accepted(string prefix)
    {
        AddServer(options => options.SignalKeyPrefix = prefix);
    }

    [Theory]
    [MemberData(nameof(BrokenSignalKeyPrefixes))]
    public void Any_other_signal_key_prefix_is_refused_with_the_name_of_its_property(string prefix)
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.SignalKeyPrefix),
            () => AddServer(options => options.SignalKeyPrefix = prefix));
    }

    [Fact]
    public void A_null_signal_key_prefix_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.SignalKeyPrefix),
            () => AddServer(options => options.SignalKeyPrefix = null!));
    }

    [Fact]
    public void A_signing_key_with_white_space_around_its_one_block_is_accepted()
    {
        string key = $" \r\n\t{CreateSigningKey()}\n \n";

        AddServer(options => options.SigningKeyPem = key);
    }

    // Each wrong kind of text once: no PEM at all, an EC key that is not in PKCS#8, a key of another algorithm, an ECDSA
    // key on another curve, and a right key with text before it, text after it or a second block after it.
    [Theory]
    [InlineData("text")]
    [InlineData("SEC1")]
    [InlineData("RSA")]
    [InlineData("P-384")]
    [InlineData("text before")]
    [InlineData("text after")]
    [InlineData("second block")]
    public void A_signing_key_that_is_not_exactly_one_PKCS8_PEM_block_of_an_ECDSA_P256_private_key_is_refused_without_the_key_in_the_message(
        string kind)
    {
        string key = CreateBrokenSigningKey(kind);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            nameof(OutpostServerOptions.SigningKeyPem),
            () => AddServer(options => options.SigningKeyPem = key));

        // A key is a secret even when it is the wrong one: no line of it is in the message.
        foreach (string line in key.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Assert.DoesNotContain(line, exception.Message, StringComparison.Ordinal);
        }
    }

    // The limit that the call validated still bounds a command after the object given to the delegate was changed.
    [Fact]
    public async Task A_change_of_the_options_after_the_call_has_no_effect()
    {
        OutpostServerOptions? given = null;

        await using ServiceProvider services = ServerSetup.Create(options =>
        {
            options.CommandSizeLimit = 4 * 1024;
            given = options;
        });

        given!.CommandSizeLimit = 16 * 1024 * 1024;

        IOutpostServer server = services.GetRequiredService<IOutpostServer>();
        OutpostReadRequest request = new() { BaseKey = "accounting", Script = new string('a', 5 * 1024) };

        Assert.Throws<ArgumentException>(() => { _ = server.ReadAsync(request, ArgumentChecks.Cancelled); });
    }

    private static void AddServer(Action<OutpostServerOptions> change)
    {
        new ServiceCollection().AddOutpostServer(options =>
        {
            ServerSetup.Require(options);
            change(options);
        });
    }

    private static void SetTime(OutpostServerOptions options, string property, TimeSpan value)
    {
        switch (property)
        {
            case nameof(OutpostServerOptions.PollTimeout):
                options.PollTimeout = value;
                break;

            case nameof(OutpostServerOptions.LeaseGrace):
                options.LeaseGrace = value;
                break;

            case nameof(OutpostServerOptions.TakeTimeout):
                options.TakeTimeout = value;
                break;

            case nameof(OutpostServerOptions.ClockSkew):
                options.ClockSkew = value;
                break;

            case nameof(OutpostServerOptions.OutcomeRetention):
                options.OutcomeRetention = value;
                break;

            case nameof(OutpostServerOptions.PairingTokenLifetime):
                options.PairingTokenLifetime = value;
                break;

            case nameof(OutpostServerOptions.PurgeInterval):
                options.PurgeInterval = value;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, "Not a time of the options.");
        }
    }

    private static void SetSize(OutpostServerOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(OutpostServerOptions.CommandSizeLimit):
                options.CommandSizeLimit = value;
                break;

            case nameof(OutpostServerOptions.OutcomeSizeLimit):
                options.OutcomeSizeLimit = value;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, "Not a size of the options.");
        }
    }

    private static string CreateBrokenSigningKey(string kind)
    {
        switch (kind)
        {
            case "text":
                return "not a key";

            case "SEC1":
                {
                    using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    return key.ExportECPrivateKeyPem();
                }

            case "RSA":
                {
                    using RSA key = RSA.Create(2048);
                    return key.ExportPkcs8PrivateKeyPem();
                }

            case "P-384":
                {
                    using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
                    return key.ExportPkcs8PrivateKeyPem();
                }

            case "text before":
                return $"Node 1\n{CreateSigningKey()}";

            case "text after":
                return $"{CreateSigningKey()}\nNode 1";

            case "second block":
                return $"{CreateSigningKey()}\n{CreateSigningKey()}";

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a kind of broken key.");
        }
    }

    // The unencrypted PKCS#8 PEM of an ECDSA P-256 private key, as the options require.
    private static string CreateSigningKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }
}
