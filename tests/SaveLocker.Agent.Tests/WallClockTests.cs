using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Tests that assert on real elapsed time (a settle gate's quiet period, a watcher's debounce) run
/// alone, after the parallel ones: beside test classes that start servers, a starved thread pool on a
/// CI runner stretches a 250 ms poll past a 10 s budget and reads as a broken gate.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockTests
{
    public const string Name = "Wall-clock timing";
}
