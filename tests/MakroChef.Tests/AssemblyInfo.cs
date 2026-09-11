using Xunit;

// Stub-server tests share static counters (StubTools.GetProductDetailsCallCount) across test
// classes; parallel execution made them flaky. The suite is small enough that serial execution
// costs nothing worth trading correctness for.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
