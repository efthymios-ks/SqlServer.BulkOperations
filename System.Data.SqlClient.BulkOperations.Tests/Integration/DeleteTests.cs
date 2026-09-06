using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class DeleteTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Delete_WhenMatchingOnTheKey_ShouldRemoveThoseRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(3, "K");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Act
        var result = await BulkOperation.Delete(books[..2])
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(2, result.Deleted);
        Assert.Equal(2, result.TotalAffected);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Delete_WhenMatchingOnANaturalKeyFromASlimDto_ShouldRemoveOnlyMatchedRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(3, "D")).ExecuteAsync(connection);

        var toDelete = new[] { new IsbnOnly { Isbn = "D0001" }, new IsbnOnly { Isbn = "D0003" } };

        var result = await BulkOperation.Delete(toDelete)
            .WithTable("Books")
            .WithMatchOn(item => item.Isbn)
            .ExecuteAsync(connection);

        Assert.Equal(2, result.Deleted);

        // Act
        var remaining = await SqlServerFixture.ScalarAsync<string>(
            connection,
            "SELECT [Isbn] FROM [dbo].[Books];"
        );

        // Assert
        Assert.Equal("D0002", remaining);
    }

    [Fact]
    public async Task Delete_WhenItemsMatchNothing_ShouldReportZeroAndLeaveTheTableAlone()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(2, "N")).ExecuteAsync(connection);

        // Act
        var result = await BulkOperation.Delete([new IsbnOnly { Isbn = "NOT-THERE" }])
            .WithTable("Books")
            .WithMatchOn(item => item.Isbn)
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(0, result.Deleted);
        Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Delete_WhenTheRowMovedOn_ShouldLeaveItInPlace()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "V");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'touched';");

        // Act
        var result = await BulkOperation.Delete(books)
            .WithConcurrencyCheck()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Delete_WhenThrowOnConcurrencyMismatchIsSetAndTheRowMovedOn_ShouldThrow()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "TEntity");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'touched';");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConcurrencyException>(() =>
            BulkOperation.Delete(books)
                .WithConcurrencyCheck()
                .WithThrowOnConcurrencyMismatch()
                .ExecuteAsync(connection));

        Assert.Equal(1, exception.Expected);
        Assert.Equal(0, exception.Actual);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Delete_WhenTheEntityUsesAGuidKeyInANonDefaultSchema_ShouldRemoveThoseRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var items = new[]
        {
            new ShopItem { Id = Guid.NewGuid(), Sku = "S1", Price = 1m, Active = true },
            new ShopItem { Id = Guid.NewGuid(), Sku = "S2", Price = 2m, Active = true }
        };

        await BulkOperation.Insert(items).ExecuteAsync(connection);

        // Act
        var result = await BulkOperation.Delete(items[..1]).ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [shop].[Items];"));
    }

    [Fact]
    public async Task Delete_WhenListIsEmpty_ShouldReportNothing()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        // Act
        var result = await BulkOperation.Delete(Array.Empty<IsbnOnly>())
            .WithMatchOn(item => item.Isbn)
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(BulkResult.Empty, result);
    }

    [Fact]
    public async Task Delete_WhenTheEntityHasNoKeyAndNoMatchOn_ShouldRejectTheConfiguration()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Delete([new IsbnOnly { Isbn = "X" }])
                .WithTable("Books")
                .ExecuteAsync(connection));

        Assert.Contains("requires match keys", exception.Message);
    }

    private const string CountBooks = "SELECT COUNT(*) FROM [dbo].[Books];";

    private static Book[] Seed(int count, string prefix)
        =>
        [
            .. Enumerable.Range(1, count).Select(index => new Book
            {
                Isbn = $"{prefix}{index:D4}",
                Title = $"Title {index}",
                Price = index,
                Edition = 1
            })
        ];
}
