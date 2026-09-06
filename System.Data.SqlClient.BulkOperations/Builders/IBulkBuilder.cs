using System.Data;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace System.Data.SqlClient.BulkOperations.Builders;

public interface IBulkBuilder<TEntity, TSelf>
    where TEntity : class
    where TSelf : IBulkBuilder<TEntity, TSelf>
{
    /// <summary>Overrides the table name taken from [Table] or the type name.</summary>
    TSelf WithTable(string tableName);

    /// <summary>Overrides the schema taken from [Table], which otherwise defaults to dbo.</summary>
    TSelf WithSchema(string schema);

    /// <summary>Restricts the operation to these columns. Call more than once to add to the set.</summary>
    TSelf WithColumns(Expression<Func<TEntity, object?>> selector);

    TSelf WithColumnMappings(IReadOnlyDictionary<string, string> mappings);

    /// <summary>Rows per bulk-copy batch. Defaults to 5000.</summary>
    TSelf WithBatchSize(int batchSize);

    /// <summary>Bulk-copy timeout in seconds; 0 means no timeout. Defaults to 300.</summary>
    TSelf WithBulkCopyTimeout(int seconds);

    /// <summary>Timeout in seconds for the generated DML; 0 means no timeout. Defaults to 300.</summary>
    TSelf WithCommandTimeout(int seconds);

    /// <summary>Replaces the default TableLock | KeepNulls options.</summary>
    TSelf WithBulkCopyOptions(SqlBulkCopyOptions options);

    /// <summary>Isolation level for the transaction this operation opens. Ignored when one is supplied.</summary>
    TSelf WithIsolationLevel(IsolationLevel isolationLevel);

    /// <summary>Transient-failure retries. Only applies when this operation owns the transaction.</summary>
    TSelf WithRetry(int maxRetries, TimeSpan? baseDelay = null);

    /// <summary>Sorts items by match key before staging so the join reads forward. On by default.</summary>
    TSelf WithOrderedKeyScan();

    /// <inheritdoc cref="WithOrderedKeyScan"/>
    TSelf WithoutOrderedKeyScan();

    /// <summary>Reports the running total of rows copied, once per batch.</summary>
    TSelf WithProgress(Action<long> rowsProcessed);

    /// <summary>Logs the generated SQL at Debug and transient retries at Warning.</summary>
    TSelf WithLogger(ILogger logger);

    /// <summary>Prefix for the staging table name. Must start with '#'. Defaults to "#bulk_".</summary>
    TSelf WithTempTablePrefix(string prefix);

    /// <summary>Runs in a transaction this operation opens and commits.</summary>
    Task<BulkResult> ExecuteAsync(SqlConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Enlists in the caller's transaction, which the caller stays responsible for committing.</summary>
    Task<BulkResult> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="ExecuteAsync(SqlConnection, SqlTransaction, CancellationToken)"/>
    Task<BulkResult> ExecuteAsync(SqlTransaction transaction, CancellationToken cancellationToken = default);
}
