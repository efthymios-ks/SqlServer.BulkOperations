using System.Data;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient.BulkOperations.Configuration;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Execution;

namespace System.Data.SqlClient.BulkOperations.Builders;

internal abstract class BulkBuilderBase<TEntity, TSelf>(BulkConfiguration<TEntity> config)
    : IBulkBuilder<TEntity, TSelf>
    where TEntity : class
    where TSelf : IBulkBuilder<TEntity, TSelf>
{
    protected BulkConfiguration<TEntity> Config { get; } = config;

    /// <summary>The concrete builder, so the shared setters can keep the fluent chain typed.</summary>
    protected abstract TSelf Self { get; }

    public TSelf WithTable(string tableName)
    {
        Config.TableOverride = tableName;

        return Self;
    }

    public TSelf WithSchema(string schema)
    {
        Config.SchemaOverride = schema;

        return Self;
    }

    public TSelf WithColumns(Expression<Func<TEntity, object?>> selector)
    {
        Config.ColumnSelectors.Add(selector);

        return Self;
    }

    public TSelf WithColumnMappings(IReadOnlyDictionary<string, string> mappings)
    {
        foreach (var mapping in mappings)
        {
            Config.ColumnMappings[mapping.Key] = mapping.Value;
        }

        return Self;
    }

    public TSelf WithBatchSize(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        Config.BatchSize = batchSize;

        return Self;
    }

    public TSelf WithBulkCopyTimeout(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        Config.BulkCopyTimeoutSeconds = seconds;

        return Self;
    }

    public TSelf WithCommandTimeout(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        Config.CommandTimeoutSeconds = seconds;

        return Self;
    }

    public TSelf WithBulkCopyOptions(SqlBulkCopyOptions options)
    {
        Config.BulkCopyOptionsOverride = options;

        return Self;
    }

    public TSelf WithIsolationLevel(IsolationLevel isolationLevel)
    {
        Config.IsolationLevel = isolationLevel;

        return Self;
    }

    public TSelf WithRetry(int maxRetries, TimeSpan? baseDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        Config.MaxRetries = maxRetries;

        if (baseDelay is not null)
        {
            Config.RetryBaseDelay = baseDelay.Value;
        }

        return Self;
    }

    public TSelf WithOrderedKeyScan()
    {
        Config.OrderedKeyScan = true;

        return Self;
    }

    public TSelf WithoutOrderedKeyScan()
    {
        Config.OrderedKeyScan = false;

        return Self;
    }

    public TSelf WithProgress(Action<long> rowsProcessed)
    {
        Config.ProgressCallback = rowsProcessed;

        return Self;
    }

    public TSelf WithLogger(ILogger logger)
    {
        Config.Logger = logger;

        return Self;
    }

    public TSelf WithTempTablePrefix(string prefix)
    {
        if (!prefix.StartsWith('#'))
        {
            var error = new BulkConfigurationException(
                message: $"Temp table prefix '{prefix}' must start with '#'; the staging table has to be session-scoped."
            );

            throw error;
        }

        Config.TempTablePrefix = prefix;

        return Self;
    }

    public Task<BulkResult> ExecuteAsync(
        SqlConnection connection,
        CancellationToken cancellationToken = default
    ) => BulkExecutor.ExecuteAsync(
        config: Config,
        connection: connection,
        transaction: null,
        cancellationToken: cancellationToken
    );

    public Task<BulkResult> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken = default
    ) => BulkExecutor.ExecuteAsync(
        config: Config,
        connection: connection,
        transaction: transaction,
        cancellationToken: cancellationToken
    );

    public Task<BulkResult> ExecuteAsync(
        SqlTransaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var connection = transaction.Connection
            ?? throw new BulkConfigurationException(
                message: "The supplied transaction has no connection; it was already committed, rolled back or disposed."
            );

        return BulkExecutor.ExecuteAsync(
            config: Config,
            connection: connection,
            transaction: transaction,
            cancellationToken: cancellationToken
        );
    }
}
