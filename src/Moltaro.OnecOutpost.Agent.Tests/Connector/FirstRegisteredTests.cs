using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Agent.Tests.Connector;

// The first finding with a registered connector, chosen once for the agent's launcher and the console (the S10 brief,
// "Internal of S10c", review 3's entry 3): in the finder's order, which gives 64 bits first, so a registration for 64
// bits wins over one for 32; a bitness with copies installed and none registered is passed over; and without any
// registration there is none.
public sealed class FirstRegisteredTests
{
    private static readonly ConnectorFile Copy64 = new(@"C:\Program Files\1cv8\8.3.27.1859\bin\comcntr.dll", "8.3.27.1859");

    private static readonly ConnectorFile Copy32 = new(@"C:\Program Files (x86)\1cv8\8.3.27.1859\bin\comcntr.dll", "8.3.27.1859");

    [Fact]
    public void A_connector_registered_for_64_bits_is_chosen_before_one_registered_for_32_bits()
    {
        ConnectorFinding first = new(ConnectorBitnessEnum.X64, Copy64, []);

        Assert.Same(first, ConnectorFinding.FirstRegistered([first, new ConnectorFinding(ConnectorBitnessEnum.X86, Copy32, [])]));
    }

    [Fact]
    public void A_bitness_with_copies_installed_and_none_registered_is_passed_over()
    {
        ConnectorFinding registered = new(ConnectorBitnessEnum.X86, Copy32, []);

        Assert.Same(
            registered,
            ConnectorFinding.FirstRegistered([new ConnectorFinding(ConnectorBitnessEnum.X64, null, [Copy64]), registered]));
    }

    [Fact]
    public void Without_a_registration_there_is_no_first_registered_finding()
    {
        Assert.Null(ConnectorFinding.FirstRegistered(
        [
            new ConnectorFinding(ConnectorBitnessEnum.X64, null, [Copy64]),
            new ConnectorFinding(ConnectorBitnessEnum.X86, null, []),
        ]));
        Assert.Null(ConnectorFinding.FirstRegistered([]));
    }
}
