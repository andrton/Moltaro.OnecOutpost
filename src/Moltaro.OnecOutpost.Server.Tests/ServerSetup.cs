using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Server.Tests;

// Options and a container that pass every check, for tests whose subject is something else. These tests never reach
// the store, so the connection string names a database that need not exist.
internal static class ServerSetup
{
    // A signing key of the form the options require, made once.
    private static readonly string SigningKeyPem = CreateSigningKeyPem();

    // Sets the options that have no default.
    public static void Require(OutpostServerOptions options)
    {
        options.ConnectionString = "Host=localhost;Database=outpost_tests";
        options.SigningKeyPem = SigningKeyPem;
    }

    // A container with the signals and the server, whose options the test may change after the required ones are set.
    public static ServiceProvider Create(Action<OutpostServerOptions>? configure = null)
    {
        ServiceCollection services = new();
        InProcessSignals.Add(services);

        services.AddOutpostServer(options =>
        {
            Require(options);
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    private static string CreateSigningKeyPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }
}
