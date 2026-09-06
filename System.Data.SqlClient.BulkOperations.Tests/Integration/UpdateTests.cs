using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class UpdateTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Update_WhenMatchingOnTheKey_ShouldWriteEveryNonKeyColumn()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(2, "K");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        foreach (var book in books)
        {
            book.Title += " (edited)";
            book.Price += 100m;
        }

        var result = await BulkOperation.Update(books)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(2, result.Updated);
        Assert.Equal(2, result.TotalAffected);

        // Act
        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Title], [Price] FROM [dbo].[Books] ORDER BY [Id];",
            reader => (Title: reader.GetString(0), Price: reader.GetDecimal(1))
        );

        // Assert
        Assert.All(stored, row => Assert.EndsWith("(edited)", row.Title));
        Assert.Equal([101m, 102m], stored.Select(row => row.Price));
    }

    [Fact]
    public async Task Update_WhenSomeItemsMatchNothing_ShouldSkipThemSilently()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(2, "U")).ExecuteAsync(connection);

        var changes = new[]
        {
            new Book { Isbn = "U0001", Title = "T1 updated", Price = 11m, Edition = 1 },
            new Book { Isbn = "MISSING", Title = "no", Price = 0m, Edition = 1 }
        };

        // Act
        var result = await BulkOperation.Update(changes)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => new { book.Title, book.Price })
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Update_WhenUpdateColumnsAreGiven_ShouldLeaveEveryOtherColumnAlone()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(1, "C")).ExecuteAsync(connection);

        var change = new[] { new Book { Isbn = "C0001", Title = "only the title", Price = 999m, Edition = 42 } };

        await BulkOperation.Update(change)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection);

        // Act
        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Title], [Price], [Edition] FROM [dbo].[Books];",
            reader => (Title: reader.GetString(0), Price: reader.GetDecimal(1), Edition: reader.GetInt32(2))
        );

        // Assert
        Assert.Equal(("only the title", 1m, 1), stored.Single());
    }

    [Fact]
    public async Task Update_WhenRequireAllMatchedIsSetAndAnItemMatchesNothing_ShouldThrowAndRollBack()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(Seed(1, "R")).ExecuteAsync(connection);

        var changes = new[]
        {
            new Book { Isbn = "R0001", Title = "changed", Price = 5m, Edition = 1 },
            new Book { Isbn = "MISSING", Title = "no", Price = 0m, Edition = 1 }
        };

        var exception = await Assert.ThrowsAsync<BulkNotMatchedException>(() =>
            BulkOperation.Update(changes)
                .WithMatchOn(book => book.Isbn)
                .WithoutConcurrencyCheck()
                .WithRequireAllMatched()
                .ExecuteAsync(connection));

        Assert.Equal(2, exception.Expected);
        Assert.Equal(1, exception.Actual);

        // Act
        var title = await SqlServerFixture.ScalarAsync<string>(
            connection,
            "SELECT [Title] FROM [dbo].[Books] WHERE [Isbn] = 'R0001';"
        );

        // Assert
        Assert.Equal("Title 1", title);
    }

    [Fact]
    public async Task Update_WhenTheConcurrencyTokensMatch_ShouldApplyTheChange()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "V");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        books[0].Title = "concurrent edit";

        // Act
        var result = await BulkOperation.Update(books)
            .WithConcurrencyCheck()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task Update_WhenTheRowMovedOn_ShouldChangeNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "S");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'someone else won';");

        books[0].Title = "stale write";

        var result = await BulkOperation.Update(books)
            .WithConcurrencyCheck()
            .ExecuteAsync(connection);

        Assert.Equal(0, result.Updated);

        // Act
        var title = await SqlServerFixture.ScalarAsync<string>(connection, "SELECT [Title] FROM [dbo].[Books];");
        Assert.Equal("someone else won", title);
    }

    [Fact]
    public async Task Update_WhenThrowOnConcurrencyMismatchIsSetAndTheRowMovedOn_ShouldThrow()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "TEntity");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        await fixture.ExecuteAsync("UPDATE [dbo].[Books] SET [Title] = 'someone else won';");

        books[0].Title = "stale write";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConcurrencyException>(() =>
            BulkOperation.Update(books)
                .WithConcurrencyCheck()
                .WithThrowOnConcurrencyMismatch()
                .ExecuteAsync(connection));

        Assert.Equal(1, exception.Expected);
        Assert.Equal(0, exception.Actual);
    }

    [Fact]
    public async Task Update_WhenOutputIdentityIsRequested_ShouldWriteTheRefreshedRowVersionBack()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(1, "W");
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        var originalRowVersion = books[0].RowVersion;
        books[0].Title = "changed";

        // Act
        await BulkOperation.Update(books)
            .WithoutConcurrencyCheck()
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Assert
        Assert.NotNull(books[0].RowVersion);
        Assert.NotEqual(originalRowVersion, books[0].RowVersion);
    }

    [Fact]
    public async Task Update_WhenColumnMappingsAreGiven_ShouldMatchAndWriteThroughMappedColumns()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert([new Widget { Name = "before", Quantity = 1 }]).ExecuteAsync(connection);

        var widgetId = await SqlServerFixture.ScalarAsync<int>(connection, "SELECT [WidgetId] FROM [dbo].[Widgets];");
        var changes = new[] { new WidgetDto { Id = widgetId, Label = "after", Count = 7 } };

        var result = await BulkOperation.Update(changes)
            .WithColumnMappings(_widgetDtoMappings)
            .ExecuteAsync(connection);

        Assert.Equal(1, result.Updated);

        // Act
        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Name], [Quantity] FROM [dbo].[Widgets];",
            reader => (Name: reader.GetString(0), Quantity: reader.GetInt32(1))
        );

        // Assert
        Assert.Equal(("after", 7), stored.Single());
    }

    [Fact]
    public async Task Update_WhenOrderedKeyScanIsTurnedOff_ShouldStillApplyEveryChange()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Seed(10, "N");
        await BulkOperation.Insert(books).ExecuteAsync(connection);

        var shuffled = books
            .OrderByDescending(book => book.Isbn)
            .Select(book => new Book { Isbn = book.Isbn, Title = "rewritten", Price = book.Price, Edition = 1 })
            .ToArray();

        // Act
        var result = await BulkOperation.Update(shuffled)
            .WithMatchOn(book => book.Isbn)
            .WithUpdateColumns(book => book.Title)
            .WithoutConcurrencyCheck()
            .WithoutOrderedKeyScan()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(10, result.Updated);
    }

    [Fact]
    public async Task Update_WhenListIsEmpty_ShouldReportNothing()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        // Act
        var result = await BulkOperation.Update(Array.Empty<Book>())
            .WithMatchOn(book => book.Isbn)
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(BulkResult.Empty, result);
    }

    [Fact]
    public async Task Update_WhenTheEntityHasNoKeyAndNoMatchOn_ShouldRejectTheConfiguration()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        var rows = new[] { new IsbnOnly { Isbn = "X" } };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Update(rows)
                .WithTable("Books")
                .ExecuteAsync(connection));

        Assert.Contains("requires match keys", exception.Message);
    }

    private const string CountBooks = "SELECT COUNT(*) FROM [dbo].[Books];";

    private static readonly Dictionary<string, string> _widgetDtoMappings = new()
    {
        ["Id"] = "WidgetId",
        ["Label"] = "Name",
        ["Count"] = "Quantity"
    };

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
