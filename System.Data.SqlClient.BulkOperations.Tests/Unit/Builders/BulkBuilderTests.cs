using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.SqlClient.BulkOperations.Builders;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Builders;

/// <summary>
/// Covers the fluent surface without a database: every option returns the same builder, and the
/// configuration checks that do not need a connection fire before one is used.
/// </summary>
public class BulkBuilderTests
{
    /// <summary>Never opened. Anything that reaches it would fail loudly rather than silently pass.</summary>
    private static readonly SqlConnection _unreachableConnection =
        new("Server=nowhere;Database=none;Connect Timeout=1;TrustServerCertificate=true;");

    [Fact]
    public void Insert_WhenSharedOptionsAreApplied_ShouldReturnTheSameBuilder()
    {
        // Arrange & Act
        var builder = BulkOperation.Insert<Book>([]);

        // Assert
        Assert.Same(builder, ApplySharedOptions(builder));
    }

    [Fact]
    public void Update_WhenEveryOptionIsApplied_ShouldReturnTheSameBuilder()
    {
        // Arrange & Act
        var builder = BulkOperation.Update<Book>([]);

        // Assert
        Assert.Same(builder, ApplySharedOptions(builder));
        Assert.Same(builder, builder.WithMatchOn(book => book.Isbn));
        Assert.Same(builder, builder.WithIdentityColumn(book => book.Id));
        Assert.Same(builder, builder.WithUpdateColumns(book => book.Title));
        Assert.Same(builder, builder.WithConcurrencyCheck());
        Assert.Same(builder, builder.WithoutConcurrencyCheck());
        Assert.Same(builder, builder.WithThrowOnConcurrencyMismatch());
        Assert.Same(builder, builder.WithRequireAllMatched());
        Assert.Same(builder, builder.WithOutput(book => book.Id));
        Assert.Same(builder, builder.WithOutputIdentity());
    }

    [Fact]
    public void Insert_WhenEveryInsertOptionIsApplied_ShouldReturnTheSameBuilder()
    {
        // Arrange & Act
        var builder = BulkOperation.Insert<Book>([]);

        // Assert
        Assert.Same(builder, builder.WithIdentityColumn(book => book.Id));
        Assert.Same(builder, builder.WithKeepIdentity());
        Assert.Same(builder, builder.WithInsertIfMissing());
        Assert.Same(builder, builder.WithInsertIfMissing(book => book.Isbn));
        Assert.Same(builder, builder.WithOutput(book => book.Id));
        Assert.Same(builder, builder.WithOutputIdentity());
    }

    [Fact]
    public void Delete_WhenEveryDeleteOptionIsApplied_ShouldReturnTheSameBuilder()
    {
        // Arrange & Act
        var builder = BulkOperation.Delete<Book>([]);

        // Assert
        Assert.Same(builder, ApplySharedOptions(builder));
        Assert.Same(builder, builder.WithMatchOn(book => book.Isbn));
        Assert.Same(builder, builder.WithConcurrencyCheck());
        Assert.Same(builder, builder.WithoutConcurrencyCheck());
        Assert.Same(builder, builder.WithThrowOnConcurrencyMismatch());
    }

    [Fact]
    public void Merge_WhenEveryMergeOptionIsApplied_ShouldReturnTheSameBuilder()
    {
        // Arrange & Act
        var builder = BulkOperation.Merge<Book>([]);

        // Assert
        Assert.Same(builder, ApplySharedOptions(builder));
        Assert.Same(builder, builder.WithMatchOn(book => book.Isbn));
        Assert.Same(builder, builder.WithInsertColumns(book => book.Isbn));
        Assert.Same(builder, builder.WithUpdateColumns(book => book.Title));
        Assert.Same(builder, builder.WithDeleteWhenNotMatched());
        Assert.Same(builder, builder.WithDuplicateKeys(DuplicateKeyBehavior.Throw));
        Assert.Same(builder, builder.WithConcurrencyCheck());
        Assert.Same(builder, builder.WithoutConcurrencyCheck());
        Assert.Same(builder, builder.WithThrowOnConcurrencyMismatch());
        Assert.Same(builder, builder.WithOutput(book => book.Id));
        Assert.Same(builder, builder.WithOutputIdentity());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithBatchSize_WhenNotPositive_ShouldThrow(int batchSize)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BulkOperation.Insert<Book>([]).WithBatchSize(batchSize));

    [Fact]
    public void WithTimeouts_WhenNegative_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BulkOperation.Insert<Book>([]).WithBulkCopyTimeout(-1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BulkOperation.Insert<Book>([]).WithCommandTimeout(-1));
    }

    [Fact]
    public void WithTimeouts_WhenZero_ShouldBeAcceptedAsNoTimeout()
    {
        // Arrange & Act
        var builder = BulkOperation.Insert<Book>([]);

        // Assert
        Assert.Same(builder, builder.WithBulkCopyTimeout(0));
        Assert.Same(builder, builder.WithCommandTimeout(0));
    }

    [Fact]
    public void WithRetry_WhenNegative_ShouldThrow()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BulkOperation.Insert<Book>([]).WithRetry(-1));

    [Fact]
    public void WithTempTablePrefix_WhenItWouldCreateAPermanentTable_ShouldThrow()
    {
        // Act & Assert
        var exception = Assert.Throws<BulkConfigurationException>(() =>
            BulkOperation.Insert<Book>([]).WithTempTablePrefix("staging_"));

        Assert.Contains("session-scoped", exception.Message);
    }

    [Fact]
    public void WithTempTablePrefix_WhenItStartsWithAHash_ShouldBeAccepted()
    {
        // Arrange & Act
        var builder = BulkOperation.Insert<Book>([]);

        // Assert
        Assert.Same(builder, builder.WithTempTablePrefix("#staging_"));
    }

    [Theory]
    [InlineData("Insert")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Merge")]
    public void Factories_WhenTheListIsNull_ShouldThrowArgumentNull(string operation)
        => Assert.Throws<ArgumentNullException>(() => _ = operation switch
        {
            "Insert" => (object)BulkOperation.Insert<Book>(null!),
            "Update" => BulkOperation.Update<Book>(null!),
            "Delete" => BulkOperation.Delete<Book>(null!),
            _ => BulkOperation.Merge<Book>(null!)
        });

    [Fact]
    public async Task ExecuteAsync_WhenTheConnectionIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BulkOperation.Insert<Book>([]).ExecuteAsync((SqlConnection)null!));

    [Fact]
    public async Task ExecuteAsync_WhenTheTransactionIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BulkOperation.Insert<Book>([]).ExecuteAsync((SqlTransaction)null!));

    [Fact]
    public async Task ExecuteAsync_WhenTheListIsEmpty_ShouldReturnEmptyWithoutTouchingTheConnection()
    {
        // Arrange & Act
        var result = await BulkOperation.Insert<Book>([]).ExecuteAsync(_unreachableConnection);

        // Assert
        Assert.Equal(BulkResult.Empty, result);
        Assert.Equal(ConnectionState.Closed, _unreachableConnection.State);
    }

    [Fact]
    public async Task ExecuteAsync_WhenKeepIdentityMeetsOutput_ShouldThrowBeforeConnecting()
        => await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert<Book>([])
                .WithKeepIdentity()
                .WithOutputIdentity()
                .ExecuteAsync(_unreachableConnection));

    [Fact]
    public async Task ExecuteAsync_WhenAnUpdateHasNoMatchKeys_ShouldThrowBeforeConnecting()
        => await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Update<IsbnOnly>([])
                .WithTable("Books")
                .ExecuteAsync(_unreachableConnection));

    [Fact]
    public async Task ExecuteAsync_WhenInsertIfMissingHasNoMatchKeys_ShouldThrowBeforeConnecting()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert<IsbnOnly>([])
                .WithTable("Books")
                .WithInsertIfMissing()
                .ExecuteAsync(_unreachableConnection));

        Assert.Contains("WithInsertIfMissing requires match keys", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnOutputColumnCannotBeWritten_ShouldThrowBeforeConnecting()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert<ReadOnlyOutput>([])
                .WithOutput(item => item.Derived)
                .ExecuteAsync(_unreachableConnection));

        Assert.Contains("read-only", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAColumnMappingIsUnknown_ShouldThrowBeforeConnecting()
        => await Assert.ThrowsAsync<BulkConfigurationException>(() =>
            BulkOperation.Insert<Book>([])
                .WithColumnMappings(new Dictionary<string, string> { ["Nope"] = "Whatever" })
                .ExecuteAsync(_unreachableConnection));

    private static TSelf ApplySharedOptions<TEntity, TSelf>(IBulkBuilder<TEntity, TSelf> builder)
        where TEntity : class
        where TSelf : IBulkBuilder<TEntity, TSelf>
        => builder
            .WithTable("Books")
            .WithSchema("dbo")
            .WithColumnMappings(new Dictionary<string, string>())
            .WithBatchSize(1_000)
            .WithBulkCopyTimeout(30)
            .WithCommandTimeout(30)
            .WithBulkCopyOptions(SqlBulkCopyOptions.Default)
            .WithIsolationLevel(IsolationLevel.Serializable)
            .WithRetry(2, TimeSpan.FromMilliseconds(10))
            .WithOrderedKeyScan()
            .WithoutOrderedKeyScan()
            .WithProgress(_ => { })
            .WithLogger(NullLogger.Instance)
            .WithTempTablePrefix("#test_");

    private class ReadOnlyOutput
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Derived
            => Name + Id;
    }
}
