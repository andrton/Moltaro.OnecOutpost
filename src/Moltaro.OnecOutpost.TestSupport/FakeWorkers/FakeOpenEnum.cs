namespace Moltaro.OnecOutpost.TestSupport.FakeWorkers;

// What the open of a fake worker gives, as the launcher's Opens decides for each process.
internal enum FakeOpenEnum
{
    // The session opened: the open gives true at once, as {"ready":true}.
    Ready,

    // No session opened: the open gives false at once, as {"ready":false}.
    NotReady,

    // The worker never answers: the open waits until Kill ends the process, and then gives false, as the end of a real
    // worker's standard output does.
    Never,
}
