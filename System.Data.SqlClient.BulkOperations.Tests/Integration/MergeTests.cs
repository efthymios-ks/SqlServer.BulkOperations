using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class MergeTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Merge_WhenSourceMixesKnownAndUnknownKeys_ShouldSplitInsertsFromUpdates()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(2, "M")).ExecuteAsync(connection);

        var payload = new[]
        {
            new Book { Isbn = "M0001", Title = "T1 merged", Price = 11m, Edition = 1 },
            new Book { Isbn = "M0003", Title = "T3", Price = 3m, Edition = 1 }
        };

        var result = await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(2, result.TotalAffected);
        Assert.Equal(3, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));

        // Act
        var updated = await SqlServerFixture.ScalarAsync<string>(
            connection,
            "SELECT [Title] FROM [dbo].[Books] WHERE [Isbn] = 'M0001';"
        );

        // Assert
        Assert.Equal("T1 merged", updated);
    }

    [Fact]
    public async Task Merge_WhenNothingMatches_ShouldInsertEveryItem()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        // Act
        var result = await BulkOperation.Merge(Seed(5, "I"))
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(5, result.Inserted);
        Assert.Equal(0, result.Updated);
    }

    [Fact]
    public async Task Merge_WhenUpdateColumnsAreRestricted_ShouldLeaveTheOtherColumnsAlone()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(1, "U")).ExecuteAsync(connection);

        var payload = new[]
        {
            new Book { Isbn = "U0001", Title = "new title", Price = 99m, Edition = 1 },
            new Book { Isbn = "U0002", Title = "new row", Price = 2m, Edition = 1 }
        };

        var result = await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);

        var matched = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Title], [Price] FROM [dbo].[Books] WHERE [Isbn] = 'U0001';",
            reader => (Title: reader.GetString(0), Price: reader.GetDecimal(1))
        );

        // Act
        // Title was in the update list, Price was not.
        Assert.Equal(("new title", 1m), matched.Single());
    }

    [Fact]
    public async Task Merge_WhenDeleteWhenNotMatchedIsSet_ShouldMakeTheTableMirrorTheSourceList()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(3, "M")).ExecuteAsync(connection);

        var payload = new[]
        {
            new Book { Isbn = "M0001", Title = "kept", Price = 1m, Edition = 1 },
            new Book { Isbn = "M0004", Title = "added", Price = 4m, Edition = 1 }
        };

        var result = await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .WithDeleteWhenNotMatched()
            .ExecuteAsync(connection);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);

        // Act
        var remaining = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Isbn] FROM [dbo].[Books] ORDER BY [Isbn];",
            reader => reader.GetString(0)
        );

        // Assert
        Assert.Equal(["M0001", "M0004"], remaining);
    }

    [Fact]
    public async Task Merge_WhenDeleteWhenNotMatchedIsCombinedWithOutput_ShouldSkipWriteBackForDeletedRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(3, "O")).ExecuteAsync(connection);

        var payload = new[]
        {
            new Book { Isbn = "O0001", Title = "kept", Price = 1m, Edition = 1 },
            new Book { Isbn = "O0009", Title = "added", Price = 9m, Edition = 1 }
        };

        // Act
        var result = await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .WithDeleteWhenNotMatched()
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);
        Assert.All(payload, book => Assert.True(book.Id > 0));
    }

    [Fact]
    public async Task Merge_WhenOutputIdentityIsRequested_ShouldWriteGeneratedValuesBackForInsertsAndUpdates()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(1, "W")).ExecuteAsync(connection);

        var payload = new[]
        {
            new Book { Isbn = "W0001", Title = "matched", Price = 1m, Edition = 1 },
            new Book { Isbn = "W0002", Title = "inserted", Price = 2m, Edition = 1 }
        };

        // Act
        await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Assert
        Assert.All(payload, book => Assert.True(book.Id > 0));
        Assert.All(payload, book => Assert.Equal(8, book.RowVersion?.Length));
        Assert.Equal(2, payload.Select(book => book.Id).Distinct().Count());
    }

    [Fact]
    public async Task Merge_WhenDuplicateKeysAreDeduplicated_ShouldKeepTheLastItemForEachKey()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var duplicates = new[]
        {
            new Book { Isbn = "DUP", Title = "first", Price = 1m, Edition = 1 },
            new Book { Isbn = "DUP", Title = "last", Price = 2m, Edition = 1 }
        };

        var result = await BulkOperation.Merge(duplicates)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(1, result.Inserted);

        // Act
        var title = await SqlServerFixture.ScalarAsync<string>(connection, "SELECT [Title] FROM [dbo].[Books];");
        Assert.Equal("last", title);
    }

    [Fact]
    public async Task Merge_WhenDuplicateKeysAreSetToThrow_ShouldRejectTheWholeBatch()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var duplicates = new[]
        {
            new Book { Isbn = "DUP", Title = "one", Price = 1m, Edition = 1 },
            new Book { Isbn = "DUP", Title = "two", Price = 2m, Edition = 1 }
        };

        // Act & Assert
        await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Merge(duplicates)
                .WithMatchOn(book => book.Isbn)
                .WithUpdateColumns(book => book.Title)
                .WithoutConcurrencyCheck()
                .WithDuplicateKeys(DuplicateKeyBehavior.Throw)
                .ExecuteAsync(connection));

        Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Merge_WhenTheRowMovedOn_ShouldNotOverwriteIt()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "V");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'someone else won';");

        books[0].Title = "stale write";

        var result = await BulkOperation.Merge(books)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(0, result.Updated);

        // Act
        var title = await SqlServerFixture.ScalarAsync<string>(connection, "SELECT [Title] FROM [dbo].[Books];");
        Assert.Equal("someone else won", title);
    }

    [Fact]
    public async Task Merge_WhenThrowOnConcurrencyMismatchIsSetAndTheRowMovedOn_ShouldThrow()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "X");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'someone else won';");

        books[0].Title = "stale write";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConcurrencyException>(() =>
            BulkOperation.Merge(books)
                .WithMatchOn(book => book.Isbn)
                .WithUpdateColumns(book => book.Title)
                .WithConcurrencyCheck()
                .WithThrowOnConcurrencyMismatch()
                .ExecuteAsync(connection));

        Assert.Equal(1, exception.Expected);
        Assert.Equal(0, exception.Actual);
    }

    [Fact]
    public async Task Merge_WhenThrowOnConcurrencyMismatchIsSetAndEveryRowApplies_ShouldNotThrow()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "Y");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        books[0].Title = "fresh write";

        // Act
        var result = await BulkOperation.Merge(books)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithConcurrencyCheck()
            .WithThrowOnConcurrencyMismatch()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task Merge_WhenDeleteWhenNotMatchedRemovesRows_ShouldNotCountThemAsApplied()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(3, "Z")).ExecuteAsync(connection);

        var payload = new[] { new Book { Isbn = "Z0001", Title = "kept", Price = 1m, Edition = 1 } };

        // Act
        // Two rows are deleted; counting them would hide a mismatch on the one row that was sent.
        var result = await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .WithDeleteWhenNotMatched()
            .WithThrowOnConcurrencyMismatch()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);
    }

    [Fact]
    public async Task Merge_WhenInsertColumnsAreGiven_ShouldWriteOnlyThoseColumnsOnInsert()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var payload = new[] { new Book { Isbn = "IC", Title = "insert cols", Price = 5m, Edition = 42 } };

        await BulkOperation.Merge(payload)
            .WithMatchOn(book => book.Isbn)
            .WithInsertColumns(book => new { book.Isbn, book.Title, book.Price })
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        // Act
        var edition = await SqlServerFixture.ScalarAsync<int>(
            connection,
            "SELECT [Edition] FROM [dbo].[Books] WHERE [Isbn] = 'IC';"
        );

        // Assert
        Assert.Equal(1, edition);
    }

    [Fact]
    public async Task Merge_WhenTheEntityUsesAGuidKeyInANonDefaultSchema_ShouldUpsertOnThatKey()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var existing = new ShopItem { Id = Guid.NewGuid(), Sku = "OLD", Price = 1m, Active = true };
        await BulkOperation.Insert([existing]).ExecuteAsync(connection);

        var payload = new[]
        {
            new ShopItem { Id = existing.Id, Sku = "UPDATED", Price = 2m, Active = false },
            new ShopItem { Id = Guid.NewGuid(), Sku = "NEW", Price = 3m, Active = true }
        };

        var result = await BulkOperation.Merge(payload).ExecuteAsync(connection);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Updated);

        // Act
        var sku = await SqlServerFixture.ScalarAsync<string>(
            connection,
            $"SELECT [Sku] FROM [shop].[Items] WHERE [Id] = '{existing.Id}';"
        );

        // Assert
        Assert.Equal("UPDATED", sku);
    }

    [Fact]
    public async Task Merge_WhenListIsEmpty_ShouldReportNothing()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        // Act
        var result = await BulkOperation.Merge(Array.Empty<Book>()).ExecuteAsync(connection);

        // Assert
        Assert.Equal(BulkResult.Empty, result);
    }

    [Fact]
    public async Task Merge_WhenTheEntityHasNoKeyAndNoMatchOn_ShouldRejectTheConfiguration()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Merge([new IsbnOnly { Isbn = "X" }])
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
