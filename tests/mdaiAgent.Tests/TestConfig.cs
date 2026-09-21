using Xunit;

// Disable parallel test execution across unit tests to prevent static state race conditions
[assembly: CollectionBehavior(DisableTestParallelization = true)]
