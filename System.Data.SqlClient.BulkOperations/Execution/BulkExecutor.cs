using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient.BulkOperations.Configuration;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class BulkExecutor
{
    /// <summary>Unit separator: a control character that will not collide with real key text.</summary>
    private const string KeySeparator = "\u001F";

    private const string NullKeyMarker = "\u0000";

    public static async Task<BulkResult> ExecuteAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(connection);

        var metadata = MetadataCache.For<TEntity>();
        var plan = OperationPlanner.Plan(config, metadata);
        Validate(config, plan);

        if (transaction is not null && !ReferenceEquals(transaction.Connection, connection))
        {
            var error = new BulkConfigurationException(
                message: "The supplied transaction does not belong to the supplied connection."
            );

            throw error;
        }

        if (config.Items.Count == 0)
        {
            return BulkResult.Empty;
        }

        // Retrying is only safe while we own the transaction; a caller's transaction is already
        // doomed by the failure, and re-running inside it would double-apply what did commit.
        var canRetry = transaction is null;
        var retries = 0;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                var result = await RunAttemptAsync(
                    config: config,
                    plan: plan,
                    connection: connection,
                    externalTransaction: transaction,
                    cancellationToken: cancellationToken
                );

                stopwatch.Stop();

                return result with
                {
                    Elapsed = stopwatch.Elapsed,
                    Retries = retries
                };
            }
            catch (Exception exception) when (canRetry
                && retries < config.MaxRetries
                && RetryPolicy.IsTransient(exception)
                && !cancellationToken.IsCancellationRequested
            )
            {
                config.Logger?.LogWarning(
                    exception,
                    "Bulk {Kind} hit a transient failure, retry {Attempt} of {MaxRetries}",
                    plan.Kind,
                    retries + 1,
                    config.MaxRetries
                );

                await Task.Delay(RetryPolicy.Backoff(config.RetryBaseDelay, retries), cancellationToken);
                retries++;
            }
            catch (Exception exception) when (RetryPolicy.FindSqlException(exception) is { } sqlException)
            {
                // SqlBulkCopy wraps server errors, so both the direct and the staged path have to
                // reach the caller as the same exception type.
                var error = new BulkExecutionException(
                    message: "Bulk operation failed: " + sqlException.Message,
                    inner: exception
                );

                throw error;
            }
        }
    }

    private static async Task<BulkResult> RunAttemptAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        OperationPlan plan,
        SqlConnection connection,
        SqlTransaction? externalTransaction,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        await using var connectionScope = await ConnectionScope.OpenAsync(connection, cancellationToken);
        await using var transactionScope = await TransactionScope.AcquireAsync(
            connection: connection,
            externalTransaction: externalTransaction,
            isolationLevel: config.IsolationLevel,
            cancellationToken: cancellationToken
        );

        var items = config.OrderedKeyScan
            ? OrderByMatchKeys(config.Items, plan.MatchColumns)
            : config.Items;

        var result = await RunOperationAsync(
            config: config,
            plan: plan,
            items: items,
            connection: connection,
            transaction: transactionScope.Transaction,
            cancellationToken: cancellationToken
        );

        await transactionScope.CommitAsync(cancellationToken);

        return result;
    }

    private static async Task<BulkResult> RunOperationAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        OperationPlan plan,
        IReadOnlyList<TEntity> items,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        if (CanUseDirectCopy(plan, config))
        {
            return await RunDirectCopyAsync(
                config: config,
                plan: plan,
                items: items,
                connection: connection,
                transaction: transaction,
                cancellationToken: cancellationToken
            );
        }

        return await RunStagedAsync(
            config: config,
            plan: plan,
            items: items,
            connection: connection,
            transaction: transaction,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>A plain insert has nothing to match or read back, so it can stream straight at the target.</summary>
    private static bool CanUseDirectCopy<TEntity>(OperationPlan plan, BulkConfiguration<TEntity> config)
        where TEntity : class
        => plan.Kind is BulkOperationKind.Insert && !config.WantsOutput && !plan.InsertIfMissing;

    private static async Task<BulkResult> RunDirectCopyAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        OperationPlan plan,
        IReadOnlyList<TEntity> items,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        await BulkCopyAsync(
            config: config,
            connection: connection,
            transaction: transaction,
            destination: plan.TargetTable,
            items: items,
            columns: plan.InsertColumns,
            withOrdinal: false,
            keepIdentity: plan.KeepIdentity,
            cancellationToken: cancellationToken
        );

        return BulkResult.Empty with
        {
            Inserted = items.Count,
            TotalAffected = items.Count
        };
    }

    private static async Task<BulkResult> RunStagedAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        OperationPlan plan,
        IReadOnlyList<TEntity> items,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        var tempTable = config.TempTablePrefix + Guid.NewGuid().ToString("N");
        var createSql = TempTableBuilder.BuildCreateTable(
            tempTableName: tempTable,
            stagingColumns: plan.StagingColumns,
            matchColumns: plan.MatchColumns,
            includeOrdinal: true,
            matchIsUnique: plan.MatchIsUnique
        );

        config.Logger?.LogDebug("Creating staging table: {Sql}", createSql);

        await ExecuteNonQueryAsync(
            connection: connection,
            transaction: transaction,
            config: config,
            sql: createSql,
            cancellationToken: cancellationToken
        );

        try
        {
            var stagedItems = Deduplicate(items, plan, config);

            await BulkCopyAsync(
                config: config,
                connection: connection,
                transaction: transaction,
                destination: tempTable,
                items: stagedItems,
                columns: plan.StagingColumns,
                withOrdinal: true,

                // The staging table has no identity of its own, so verbatim values are what we want.
                keepIdentity: true,
                cancellationToken: cancellationToken
            );

            var statement = BuildStatement(plan, tempTable);
            config.Logger?.LogDebug("Executing bulk DML: {Sql}", statement.Sql);

            var result = statement.ReturnsRows
                ? await ReadOutputAsync(
                    config: config,
                    connection: connection,
                    transaction: transaction,
                    sql: statement.Sql,
                    plan: plan,
                    items: stagedItems,
                    cancellationToken: cancellationToken
                )
                : await CountAffectedAsync(
                    config: config,
                    connection: connection,
                    transaction: transaction,
                    sql: statement.Sql,
                    plan: plan,
                    cancellationToken: cancellationToken
                );

            GuardRowCounts(config, plan, expected: stagedItems.Count, result);

            return result;
        }
        finally
        {
            await DropStagingTableAsync(config, connection, transaction, tempTable);
        }
    }

    private static async Task<BulkResult> CountAffectedAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        OperationPlan plan,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        var affected = await ExecuteNonQueryAsync(
            connection: connection,
            transaction: transaction,
            config: config,
            sql: sql,
            cancellationToken: cancellationToken
        );

        return Attribute(plan.Kind, affected);
    }

    private static BulkResult Attribute(BulkOperationKind kind, int affected)
        => kind switch
        {
            BulkOperationKind.Update => BulkResult.Empty with { Updated = affected, TotalAffected = affected },
            BulkOperationKind.Delete => BulkResult.Empty with { Deleted = affected, TotalAffected = affected },
            _ => BulkResult.Empty with { Inserted = affected, TotalAffected = affected }
        };

    private static void GuardRowCounts<TEntity>(
        BulkConfiguration<TEntity> config,
        OperationPlan plan,
        int expected,
        BulkResult result
    ) where TEntity : class
    {
        if (config.ThrowOnConcurrencyMismatch)
        {
            var applied = AppliedRowCount(plan.Kind, result);

            if (applied < expected)
            {
                var error = new BulkConcurrencyException(expected, applied);

                throw error;
            }
        }

        if (config.RequireAllMatched
            && plan.Kind is BulkOperationKind.Update
            && result.TotalAffected < expected
        )
        {
            var error = new BulkNotMatchedException(expected, result.TotalAffected);

            throw error;
        }
    }

    /// <summary>
    /// Rows the statement actually applied. A merge is counted as inserts plus updates rather than
    /// by the total: a row whose concurrency token failed produces no $action at all, so the
    /// shortfall is exactly the mismatches, and rows removed by WithDeleteWhenNotMatched are not
    /// mistaken for ones that succeeded.
    /// </summary>
    private static int AppliedRowCount(BulkOperationKind kind, BulkResult result)
        => kind is BulkOperationKind.Merge
            ? result.Inserted + result.Updated
            : result.TotalAffected;

    private static async Task DropStagingTableAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        SqlConnection connection,
        SqlTransaction transaction,
        string tempTable
    ) where TEntity : class
    {
        try
        {
            await ExecuteNonQueryAsync(
                connection: connection,
                transaction: transaction,
                config: config,
                sql: "DROP TABLE IF EXISTS " + SqlIdentifier.Quote(tempTable) + ";",

                // The drop must still run while the original failure is unwinding.
                cancellationToken: CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            // A session-scoped temp table dies with the connection, so this is never fatal.
            config.Logger?.LogDebug(exception, "Could not drop staging table {TempTable}", tempTable);
        }
    }

    private static IReadOnlyList<TEntity> Deduplicate<TEntity>(
        IReadOnlyList<TEntity> items,
        OperationPlan plan,
        BulkConfiguration<TEntity> config
    ) where TEntity : class
    {
        // Only MERGE needs this: SQL Server rejects a merge whose source matches a target row twice.
        if (plan.Kind is not BulkOperationKind.Merge || plan.MatchColumns.Count == 0)
        {
            return items;
        }

        if (config.DuplicateKeys is DuplicateKeyBehavior.Deduplicate)
        {
            var lastPerKey = new Dictionary<string, TEntity>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                lastPerKey[BuildKey(item, plan.MatchColumns)] = item;
            }

            return [.. lastPerKey.Values];
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (!seenKeys.Add(BuildKey(item, plan.MatchColumns)))
            {
                var error = new BulkConfigurationException(
                    message: "Duplicate match keys detected under DuplicateKeyBehavior.Throw."
                );

                throw error;
            }
        }

        return items;
    }

    private static string BuildKey<TEntity>(TEntity item, IReadOnlyList<ColumnMetadata> matchColumns)
        => string.Join(
            KeySeparator,
            matchColumns.Select(column => column.Getter(item!)?.ToString() ?? NullKeyMarker)
        );

    /// <summary>
    /// Feeding the staging table in key order keeps the later join a forward scan instead of a
    /// random walk. Skipped when a key type has no natural ordering, since nothing would be gained.
    /// </summary>
    private static IReadOnlyList<TEntity> OrderByMatchKeys<TEntity>(
        IReadOnlyList<TEntity> items,
        IReadOnlyList<ColumnMetadata> matchColumns
    ) where TEntity : class
    {
        if (matchColumns.Count == 0 || !matchColumns.All(IsOrderable))
        {
            return items;
        }

        IOrderedEnumerable<TEntity>? ordered = null;

        foreach (var column in matchColumns)
        {
            ordered = ordered is null
                ? items.OrderBy(item => column.Getter(item!))
                : ordered.ThenBy(item => column.Getter(item!));
        }

        return [.. ordered!];
    }

    private static bool IsOrderable(ColumnMetadata column)
    {
        var propertyType = column.Property.PropertyType;

        return typeof(IComparable).IsAssignableFrom(Nullable.GetUnderlyingType(propertyType) ?? propertyType);
    }

    private static BuiltStatement BuildStatement(OperationPlan plan, string tempTable)
        => plan.Kind switch
        {
            BulkOperationKind.Insert when plan.InsertIfMissing
                => SqlStatementBuilder.BuildInsertIfMissing(
                    targetQualified: plan.TargetTable,
                    tempTable: tempTable,
                    insertColumns: plan.InsertColumns,
                    matchColumns: plan.MatchColumns,
                    outputColumns: plan.OutputColumns
                ),
            BulkOperationKind.Insert
                => SqlStatementBuilder.BuildInsertWithOutput(
                    targetQualified: plan.TargetTable,
                    tempTable: tempTable,
                    insertColumns: plan.InsertColumns,
                    outputColumns: plan.OutputColumns
                ),
            BulkOperationKind.Update
                => SqlStatementBuilder.BuildUpdate(
                    targetQualified: plan.TargetTable,
                    tempTable: tempTable,
                    updateColumns: plan.UpdateColumns,
                    matchColumns: plan.MatchColumns,
                    concurrencyColumns: plan.ConcurrencyColumns,
                    outputColumns: plan.OutputColumns
                ),
            BulkOperationKind.Delete
                => SqlStatementBuilder.BuildDelete(
                    targetQualified: plan.TargetTable,
                    tempTable: tempTable,
                    matchColumns: plan.MatchColumns,
                    concurrencyColumns: plan.ConcurrencyColumns
                ),
            BulkOperationKind.Merge
                => SqlStatementBuilder.BuildMerge(
                    targetQualified: plan.TargetTable,
                    tempTable: tempTable,
                    insertColumns: plan.InsertColumns,
                    updateColumns: plan.UpdateColumns,
                    matchColumns: plan.MatchColumns,
                    concurrencyColumns: plan.ConcurrencyColumns,
                    outputColumns: plan.OutputColumns,
                    deleteWhenNotMatched: plan.DeleteWhenNotMatched
                ),
            _ => throw new NotSupportedException($"Unsupported bulk operation kind '{plan.Kind}'.")
        };

    private static async Task BulkCopyAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        SqlConnection connection,
        SqlTransaction transaction,
        string destination,
        IReadOnlyList<TEntity> items,
        IReadOnlyList<ColumnMetadata> columns,
        bool withOrdinal,
        bool keepIdentity,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        using var bulkCopy = new SqlBulkCopy(
            connection: connection,
            copyOptions: BuildBulkCopyOptions(config, keepIdentity),
            externalTransaction: transaction
        )
        {
            DestinationTableName = destination,
            BatchSize = config.BatchSize,
            BulkCopyTimeout = config.BulkCopyTimeoutSeconds,
            EnableStreaming = true
        };

        if (withOrdinal)
        {
            bulkCopy.ColumnMappings.Add(BulkColumns.Ordinal, BulkColumns.Ordinal);
        }

        foreach (var column in columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        if (config.ProgressCallback is { } progressCallback)
        {
            bulkCopy.NotifyAfter = config.BatchSize;
            bulkCopy.SqlRowsCopied += (_, eventArgs) => progressCallback(eventArgs.RowsCopied);
        }

        using var dataReader = new BulkDataReader<TEntity>(
            items: items,
            columns: columns,
            withOrdinal: withOrdinal
        );

        await bulkCopy.WriteToServerAsync(dataReader, cancellationToken);
    }

    private static SqlBulkCopyOptions BuildBulkCopyOptions<TEntity>(
        BulkConfiguration<TEntity> config,
        bool keepIdentity
    ) where TEntity : class
    {
        // KeepNulls stops SQL Server substituting column defaults for values the caller set to null.
        var options = config.BulkCopyOptionsOverride
            ?? (SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.KeepNulls);

        return keepIdentity
            ? options | SqlBulkCopyOptions.KeepIdentity
            : options;
    }

    private static async Task<int> ExecuteNonQueryAsync<TEntity>(
        SqlConnection connection,
        SqlTransaction transaction,
        BulkConfiguration<TEntity> config,
        string sql,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        await using var command = new SqlCommand(
            cmdText: sql,
            connection: connection,
            transaction: transaction
        )
        {
            CommandTimeout = config.CommandTimeoutSeconds
        };

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Runs a statement whose OUTPUT clause streams back the affected rows, writing store-generated
    /// values onto the originating objects via the ordinal each staging row carries.
    /// </summary>
    private static async Task<BulkResult> ReadOutputAsync<TEntity>(
        BulkConfiguration<TEntity> config,
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        OperationPlan plan,
        IReadOnlyList<TEntity> items,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        // Only MERGE prefixes each row with $action; everything else has a single known outcome.
        var actionOffset = plan.Kind is BulkOperationKind.Merge ? 1 : 0;
        var ordinalIndex = actionOffset + plan.OutputColumns.Count;

        var inserted = 0;
        var updated = 0;
        var deleted = 0;
        var total = 0;

        await using var command = new SqlCommand(
            cmdText: sql,
            connection: connection,
            transaction: transaction
        )
        {
            CommandTimeout = config.CommandTimeoutSeconds
        };

        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken);

        do
        {
            while (await dataReader.ReadAsync(cancellationToken))
            {
                total++;

                var action = actionOffset > 0
                    ? dataReader.GetString(0)
                    : null;

                switch (action)
                {
                    case MergeAction.Update:
                        updated++;
                        break;
                    case MergeAction.Delete:
                        deleted++;
                        break;
                    case MergeAction.Insert:
                    case null when plan.Kind is BulkOperationKind.Insert:
                        inserted++;
                        break;
                    case null when plan.Kind is BulkOperationKind.Update:
                        updated++;
                        break;
                    case null when plan.Kind is BulkOperationKind.Delete:
                        deleted++;
                        break;
                }

                await WriteBackAsync(
                    dataReader: dataReader,
                    plan: plan,
                    items: items,
                    action: action,
                    ordinalIndex: ordinalIndex,
                    actionOffset: actionOffset,
                    cancellationToken: cancellationToken
                );
            }
        } while (await dataReader.NextResultAsync(cancellationToken));

        return new BulkResult(
            Inserted: inserted,
            Updated: updated,
            Deleted: deleted,
            TotalAffected: total,
            Elapsed: TimeSpan.Zero,
            Retries: 0
        );
    }

    private static async Task WriteBackAsync<TEntity>(
        SqlDataReader dataReader,
        OperationPlan plan,
        IReadOnlyList<TEntity> items,
        string? action,
        int ordinalIndex,
        int actionOffset,
        CancellationToken cancellationToken
    ) where TEntity : class
    {
        if (plan.OutputColumns.Count == 0)
        {
            return;
        }

        // A row deleted by WHEN NOT MATCHED BY SOURCE has no source row, so its ordinal and its
        // INSERTED columns are all NULL and there is nothing to write anything back onto.
        if (action is MergeAction.Delete || await dataReader.IsDBNullAsync(ordinalIndex, cancellationToken))
        {
            return;
        }

        var item = items[dataReader.GetInt32(ordinalIndex)];

        for (var columnIndex = 0; columnIndex < plan.OutputColumns.Count; columnIndex++)
        {
            var column = plan.OutputColumns[columnIndex];
            var resultIndex = actionOffset + columnIndex;

            var rawValue = await dataReader.IsDBNullAsync(resultIndex, cancellationToken)
                ? null
                : dataReader.GetValue(resultIndex);

            column.Setter!(item!, SqlTypeMapper.FromStoreValue(rawValue, column.Property.PropertyType));
        }
    }

    private static void Validate<TEntity>(BulkConfiguration<TEntity> config, OperationPlan plan)
        where TEntity : class
    {
        if (config.KeepIdentity && config.WantsOutput)
        {
            var error = new BulkConfigurationException(
                message: "WithKeepIdentity cannot be combined with WithOutput/WithOutputIdentity."
            );

            throw error;
        }

        if (plan.MatchColumns.Count == 0)
        {
            if (plan.Kind is BulkOperationKind.Update or BulkOperationKind.Delete or BulkOperationKind.Merge)
            {
                var error = new BulkConfigurationException(
                    message: $"{plan.Kind} requires match keys. Call WithMatchOn or annotate a [Key] property."
                );

                throw error;
            }

            if (plan.InsertIfMissing)
            {
                var error = new BulkConfigurationException(
                    message: "WithInsertIfMissing requires match keys. Pass a selector or annotate a [Key] property."
                );

                throw error;
            }
        }

        foreach (var column in plan.OutputColumns)
        {
            if (column.Setter is null)
            {
                var error = new BulkConfigurationException(
                    message: $"Output column '{column.PropertyName}' is read-only, so its value cannot be written back."
                );

                throw error;
            }
        }
    }

    private static class MergeAction
    {
        public const string Insert = "INSERT";

        public const string Update = "UPDATE";

        public const string Delete = "DELETE";
    }
}
