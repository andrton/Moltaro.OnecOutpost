namespace Moltaro.OnecOutpost.Console.Service;

// How a stop of the service ended: the service does not exist, it is stopped, or it did not stop within the timeout.
internal enum ServiceStopEnum
{
    NotInstalled,
    Stopped,
    TimedOut,
}
