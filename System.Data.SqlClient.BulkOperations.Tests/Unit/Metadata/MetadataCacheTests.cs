using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Metadata;

/// <summary>
/// The cache is process-wide, so these tests clear it and must not run beside anything that reads it.
/// </summary>
[Collection(nameof(MetadataCacheTests))]
public class MetadataCacheTests
{
    [Fact]
    public void For_WhenAskedTwice_ShouldReturnTheSameInstance()
    {
        // Arrange & Act
        var first = MetadataCache.For<Book>();
        var second = MetadataCache.For<Book>();

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void For_WhenAskedByTypeAndByGeneric_ShouldReturnTheSameInstance()
        => Assert.Same(MetadataCache.For<Book>(), MetadataCache.For<Book>());

    [Fact]
    public async Task For_WhenFirstUsedConcurrently_ShouldBuildExactlyOneInstance()
    {
        // Arrange
        MetadataCache.Clear();

        // Act
        var builds = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => MetadataCache.For<Widget>()))
        );

        // Assert
        Assert.Single(builds.Distinct());
    }

    [Fact]
    public void Clear_WhenTheTypeIsRequestedAgain_ShouldRebuildIt()
    {
        // Arrange & Act
        var before = MetadataCache.For<ShopItem>();
        MetadataCache.Clear();
        var after = MetadataCache.For<ShopItem>();

        // Assert
        Assert.NotSame(before, after);
    }
}

[CollectionDefinition(nameof(MetadataCacheTests), DisableParallelization = true)]
public sealed class MetadataCacheCollection;
