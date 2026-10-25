using SaveLocker.Agent.Linux;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Which directories the updater fsyncs after copying a file. A directory it had to create is a new
/// name in its parent, so every level up to the install prefix has to be flushed. Missing one means
/// a crash can lose a whole subdirectory of the new version.
/// </summary>
public sealed class DurableTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "prefix");

    [Fact]
    public void UpTo_ListsEveryLevel_FromTheFilesDirectoryToTheRoot()
    {
        var dir = Path.Combine(Root, "agent-ui", "assets");
        Assert.Equal(
            [dir, Path.Combine(Root, "agent-ui"), Root],
            Durable.UpTo(dir, Root));
    }

    [Fact]
    public void UpTo_TheRootItself_IsOneEntry()
    {
        Assert.Equal([Root], Durable.UpTo(Root, Root));
    }

    [Fact]
    public void UpTo_IgnoresATrailingSeparator()
    {
        Assert.Equal([Root], Durable.UpTo(Root + Path.DirectorySeparatorChar, Root));
    }

    [Fact]
    public void UpTo_ADirectoryOutsideTheRoot_FlushesNothing()
    {
        Assert.Empty(Durable.UpTo(Path.Combine(Path.GetTempPath(), "elsewhere"), Root));
    }

    [Fact]
    public void UpTo_ASiblingSharingTheRootsNamePrefix_IsOutside()
    {
        Assert.Empty(Durable.UpTo(Root + "-old", Root));
    }
}
