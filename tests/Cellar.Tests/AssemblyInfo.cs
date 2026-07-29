// Tests render into shared state (the ambient Theme.Current global), so running test
// collections in parallel can race — a theme-swap test mutating Theme.Current while a
// rendering test reads it. Disable parallelization to keep the suite deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
