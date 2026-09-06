using Microsoft.Data.SqlClient;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class TransactionTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Execute_WhenTheCallerSuppliesATransaction_ShouldNotCommitIt()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await BulkOperation.Insert([Book("T1")]).ExecuteAsync(connection, transaction);

        // Act
        await transaction.RollbackAsync();

        // Assert
        Assert.Equal(0, await fixture.ScalarAsync<int>(CountBooks));
    }

    [Fact]
    public async Task Execute_WhenTheCallerCommitsTheirTransaction_ShouldPersistTheRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await BulkOperation.Insert([Book("T2")]).ExecuteAsync(connection, transaction);

        // Act
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(1, await fixture.ScalarAsync<int>(CountBooks));
    }

    [Fact]
    public async Task Execute_WhenSeveralOperationsShareOneTransaction_ShouldApplyThemAsAUnit()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var books = new[] { Book("U1"), Book("U2") };
        await BulkOperation.Insert(books)
            .WithOutputIdentity()
            .ExecuteAsync(connection, transaction);

        books[0].Title = "changed inside the transaction";
        await BulkOperation.Update(books[..1])
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection, transaction);

        await BulkOperation.Delete(books[1..])
            .WithoutConcurrencyCheck()
            .ExecuteAsync(connection, transaction);

        await transaction.CommitAsync();

        // Act
        var stored = await SqlServerFixture.QueryAsync(
            connection,
            "SELECT [Isbn], [Title] FROM [dbo].[Books];",
            reader => (Isbn: reader.GetString(0), Title: reader.GetString(1))
        );

        // Assert
        Assert.Equal(("U1", "changed inside the transaction"), stored.Single());
    }

    [Fact]
    public async Task Execute_WhenOnlyATransactionIsGiven_ShouldUseItsConnection()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await BulkOperation.Insert([Book("T3")]).ExecuteAsync(transaction);

        // Act
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(1, await fixture.ScalarAsync<int>(CountBooks));
    }

    [Fact]
    public async Task Execute_WhenTheOperationOwnsTheTransaction_ShouldRollBackEverythingOnFailure()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        await BulkOperation.Insert([Book("R1")]).ExecuteAsync(connection);

        // Act
        // The unique index on Isbn rejects the second row, which must undo the first as well.
        await Assert.ThrowsAsync<BulkExecutionException>(() =>
            BulkOperation.Insert([Book("R2"), Book("R1")])
                .WithRetry(maxRetries: 0)
                .ExecuteAsync(connection));

        // Assert
        Assert.Equal(1, await fixture.ScalarAsync<int>(CountBooks));
    }

    [Fact]
    public async Task Execute_WhenTheTransactionBelongsToAnotherConnection_ShouldRejectTheConfiguration()
    {
        // Arrange
        await using var first = await fixture.OpenConnectionAsync();
        await using var second = await fixture.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await second.BeginTransactionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert([Book("X")]).ExecuteAsync(first, transaction));

        Assert.Contains("does not belong to the supplied connection", exception.Message);
    }

    [Fact]
    public async Task Execute_WhenCancelled_ShouldNotLeaveRowsBehind()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var connection = await fixture.OpenConnectionAsync();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BulkOperation.Insert([Book("C1")]).ExecuteAsync(connection, cancellation.Token));

        Assert.Equal(0, await fixture.ScalarAsync<int>(CountBooks));
    }

    private const string CountBooks = "SELECT COUNT(*) FROM [dbo].[Books];";

    private static Book Book(string isbn)
        => new() { Isbn = isbn, Title = "title " + isbn, Price = 1m, Edition = 1 };
}
