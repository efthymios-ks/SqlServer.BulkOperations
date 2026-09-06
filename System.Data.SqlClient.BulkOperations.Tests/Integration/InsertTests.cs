using Microsoft.Data.SqlClient;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class InsertTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Insert_WhenNoOutputRequested_ShouldTakeTheDirectCopyPathAndWriteEveryRow()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 100, prefix: "F");

        // Act
        var result = await BulkOperation.Insert(books).ExecuteAsync(connection);

        // Assert
        Assert.Equal(100, result.Inserted);
        Assert.Equal(100, result.TotalAffected);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, result.Retries);
        Assert.Equal(100, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Insert_WhenListIsEmpty_ShouldReportNothingAndWriteNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        // Act
        var result = await BulkOperation.Insert(Array.Empty<Book>()).ExecuteAsync(connection);

        // Assert
        Assert.Equal(BulkResult.Empty, result);
        Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Insert_WhenOutputIdentityIsRequested_ShouldWriteGeneratedValuesBackOntoEveryItem()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 5, prefix: "O");

        // Act
        var result = await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(5, result.Inserted);
        Assert.All(books, book => Assert.True(book.Id > 0));
        Assert.All(books, book => Assert.NotEqual(default, book.CreatedUtc));
        Assert.All(books, book => Assert.Equal(8, book.RowVersion?.Length));
        Assert.Equal(5, books.Select(book => book.Id).Distinct().Count());
    }

    [Fact]
    public async Task Insert_WhenOutputIdentityIsRequested_ShouldMatchGeneratedIdsToTheRightItems()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 20, prefix: "M");

        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Id], [Isbn] FROM [dbo].[Books];",
            reader => (Id: reader.GetInt32(0), Isbn: reader.GetString(1))
        );

        // Act
        var isbnById = stored.ToDictionary(row => row.Id, row => row.Isbn);
        Assert.All(books, book => Assert.Equal(book.Isbn, isbnById[book.Id]));
    }

    [Fact]
    public async Task Insert_WhenOnlyOneOutputColumnIsRequested_ShouldWriteBackOnlyThatColumn()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 3, prefix: "P");

        // Act
        await BulkOperation.Insert(books)
            .WithOutput(book => book.Id)
            .ExecuteAsync(connection);

        // Assert
        Assert.All(books, book => Assert.True(book.Id > 0));
        Assert.All(books, book => Assert.Equal(default, book.CreatedUtc));
    }

    [Fact]
    public async Task Insert_WhenKeepIdentityIsSet_ShouldPersistTheSuppliedIdentityValues()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = new[]
        {
            new Book { Id = 500, Isbn = "K1", Title = "explicit id", Price = 1m, Edition = 1 },
            new Book { Id = 501, Isbn = "K2", Title = "explicit id", Price = 2m, Edition = 1 }
        };

        var result = await BulkOperation.Insert(books)
            .WithKeepIdentity()
            .ExecuteAsync(connection);

        Assert.Equal(2, result.Inserted);

        // Act
        var storedIds = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Id] FROM [dbo].[Books] ORDER BY [Id];",
            reader => reader.GetInt32(0)
        );

        // Assert
        Assert.Equal([500, 501], storedIds);
    }

    [Fact]
    public async Task Insert_WhenInsertIfMissingIsSet_ShouldAddOnlyTheUnseenKeys()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert(
        [
            new Book { Isbn = "AAA", Title = "A", Price = 1m, Edition = 1 },
            new Book { Isbn = "BBB", Title = "B", Price = 2m, Edition = 1 }
        ]).ExecuteAsync(connection);

        var candidates = new[]
        {
            new Book { Isbn = "AAA", Title = "A rewritten", Price = 9m, Edition = 1 },
            new Book { Isbn = "CCC", Title = "C", Price = 3m, Edition = 1 }
        };

        var result = await BulkOperation.Insert(candidates)
            .WithInsertIfMissing(book => book.Isbn)
            .ExecuteAsync(connection);

        Assert.Equal(1, result.TotalAffected);
        Assert.Equal(3, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));

        // Act
        var untouched = await SqlServerFixture.ScalarAsync<string>(
            connection,
            "SELECT [Title] FROM [dbo].[Books] WHERE [Isbn] = 'AAA';"
        );

        // Assert
        Assert.Equal("A", untouched);
    }

    [Fact]
    public async Task Insert_WhenInsertIfMissingIsCombinedWithOutput_ShouldWriteBackOnlyForRowsItInserted()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert([new Book { Isbn = "EXISTS", Title = "seed", Price = 1m, Edition = 1 }])
            .ExecuteAsync(connection);

        var candidates = new[]
        {
            new Book { Isbn = "EXISTS", Title = "skipped", Price = 1m, Edition = 1 },
            new Book { Isbn = "FRESH", Title = "inserted", Price = 2m, Edition = 1 }
        };

        // Act
        await BulkOperation.Insert(candidates)
            .WithInsertIfMissing(book => book.Isbn)
            .WithOutputIdentity()
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(0, candidates[0].Id);
        Assert.True(candidates[1].Id > 0);
    }

    [Fact]
    public async Task Insert_WhenAColumnWhitelistIsGiven_ShouldLeaveUnlistedColumnsAtTheirDatabaseDefault()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var books = new[] { new Book { Isbn = "SUB", Title = "subset", Price = 7m, Edition = 99 } };

        await BulkOperation.Insert(books)
            .WithColumns(book => new { book.Isbn, book.Title, book.Price })
            .ExecuteAsync(connection);

        // Act
        var edition = await SqlServerFixture.ScalarAsync<int>(
            connection,
            "SELECT [Edition] FROM [dbo].[Books] WHERE [Isbn] = 'SUB';"
        );

        // Assert
        Assert.Equal(1, edition);
    }

    [Fact]
    public async Task Insert_WhenColumnMappingsAreGiven_ShouldWriteThroughToTheMappedColumns()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var widgets = new[]
        {
            new WidgetDto { Label = "mapped one", Count = 11 },
            new WidgetDto { Label = "mapped two", Count = 22 }
        };

        var result = await BulkOperation.Insert(widgets)
            .WithColumnMappings(_widgetDtoMappings)
            .ExecuteAsync(connection);

        Assert.Equal(2, result.Inserted);

        // Act
        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Name], [Quantity] FROM [dbo].[Widgets] ORDER BY [Quantity];",
            reader => (Name: reader.GetString(0), Quantity: reader.GetInt32(1))
        );

        // Assert
        Assert.Equal([("mapped one", 11), ("mapped two", 22)], stored);
    }

    [Fact]
    public async Task Insert_WhenTheEntityUsesANonDefaultSchemaAndGuidKey_ShouldWriteToThatTable()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var items = new[]
        {
            new ShopItem { Id = Guid.NewGuid(), Sku = "SKU-1", Price = 10m, Active = true },
            new ShopItem { Id = Guid.NewGuid(), Sku = "SKU-2", Price = 20m, Active = false }
        };

        // Act
        var result = await BulkOperation.Insert(items).ExecuteAsync(connection);

        // Assert
        Assert.Equal(2, result.Inserted);
        Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [shop].[Items];"));
    }

    [Fact]
    public async Task Insert_WhenAPropertyIsAnEnum_ShouldStoreTheUnderlyingIntegralValue()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var widgets = new[] { new Widget { Name = "enum", Quantity = 1, Status = WidgetStatus.Retired } };

        await BulkOperation.Insert(widgets).ExecuteAsync(connection);

        // Act
        var status = await SqlServerFixture.ScalarAsync<int>(
            connection,
            "SELECT [Status] FROM [dbo].[Widgets] WHERE [Name] = 'enum';"
        );

        // Assert
        Assert.Equal((int)WidgetStatus.Retired, status);
    }

    [Fact]
    public async Task Insert_WhenTheTableIsOverridden_ShouldTargetTheNamedTable()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var rows = new[] { new BookRow { Isbn = "OVERRIDE", Title = "named target", Price = 1m } };

        // Act
        await BulkOperation.Insert(rows)
            .WithTable("Books")
            .WithSchema("dbo")
            .ExecuteAsync(connection);

        // Assert
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connection, CountBooks));
    }

    [Fact]
    public async Task Insert_WhenProgressIsRequested_ShouldReportTheRunningRowCount()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        var reported = new List<long>();
        var books = Books(count: 250, prefix: "PR");

        // Act
        await BulkOperation.Insert(books)
            .WithBatchSize(100)
            .WithProgress(reported.Add)
            .ExecuteAsync(connection);

        // Assert
        Assert.NotEmpty(reported);
        Assert.Equal(reported.OrderBy(rows => rows), reported);
        Assert.True(reported[^1] <= 250);
    }

    [Fact]
    public async Task Insert_WhenKeepIdentityIsCombinedWithOutput_ShouldRejectTheConfiguration()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 1, prefix: "X");

        // Act & Assert
        await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert(books)
                .WithKeepIdentity()
                .WithOutputIdentity()
                .ExecuteAsync(connection));
    }

    [Fact]
    public async Task Insert_WhenTheTableDoesNotExist_ShouldSurfaceAnExecutionException()
    {
        // Arrange
        await using var connection = await fixture.OpenConnectionAsync();

        var books = Books(count: 1, prefix: "X");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkExecutionException>(() =>
            BulkOperation.Insert(books)
                .WithTable("NoSuchTable")
                .ExecuteAsync(connection));

        Assert.NotNull(exception.InnerException);
        Assert.Contains("NoSuchTable", exception.Message);
    }

    [Fact]
    public async Task Insert_WhenConnectionIsClosed_ShouldOpenItAndCloseItAgain()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = new SqlConnection(fixture.ConnectionString);

        // Act
        await BulkOperation.Insert(Books(count: 2, prefix: "CL")).ExecuteAsync(connection);

        // Assert
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Equal(2, await fixture.ScalarAsync<int>(CountBooks));
    }

    private const string CountBooks = "SELECT COUNT(*) FROM [dbo].[Books];";

    private static readonly Dictionary<string, string> _widgetDtoMappings = new()
    {
        ["Id"] = "WidgetId",
        ["Label"] = "Name",
        ["Count"] = "Quantity"
    };

    private static Book[] Books(int count, string prefix)
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
