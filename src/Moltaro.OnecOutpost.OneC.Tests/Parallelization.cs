using Xunit.Sdk;
using Xunit.v3;

// The tests run one at a time: every session of an external connection takes a client licence from the pool of the
// lab's 1C server (concept §12, O3), and the tests of one class or of several at once could take more than it has.
[assembly: Parallelization(Mode = ParallelMode.None)]
