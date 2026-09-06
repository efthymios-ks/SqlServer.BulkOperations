using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Configuration;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Expressions;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class OperationPlanner
{
    public static OperationPlan Plan<TEntity>(BulkConfiguration<TEntity> config, EntityMetadata metadata) where TEntity : class
    {
        var baseColumns = SelectBaseColumns(config, metadata);
        var identityOverride = ResolveIdentityOverride(config, baseColumns);
        var matchColumns = ResolveMatchColumns(config, metadata, baseColumns);
        var insertColumns = ResolveInsertColumns(config, metadata, baseColumns, identityOverride);
        var updateColumns = ResolveUpdateColumns(config, metadata, baseColumns, identityOverride);
        var concurrencyColumns = ResolveConcurrencyColumns(config, metadata, baseColumns);
        var outputColumns = ResolveOutputColumns(config, metadata, baseColumns);

        return new OperationPlan
        {
            Kind = config.Kind,
            TargetTable = SqlIdentifier.QualifiedName(
                schema: config.SchemaOverride ?? metadata.Schema,
                table: config.TableOverride ?? metadata.TableName
            ),
            InsertColumns = insertColumns,
            UpdateColumns = updateColumns,
            MatchColumns = matchColumns,
            ConcurrencyColumns = concurrencyColumns,
            OutputColumns = outputColumns,
            StagingColumns = ResolveStagingColumns(
                kind: config.Kind,
                matchColumns: matchColumns,
                insertColumns: insertColumns,
                updateColumns: updateColumns,
                concurrencyColumns: concurrencyColumns
            ),
            KeepIdentity = config.KeepIdentity,
            InsertIfMissing = config.InsertIfMissing,
            DeleteWhenNotMatched = config.DeleteWhenNotMatched,

            // A merge is de-duplicated before staging; otherwise only a real key guarantees uniqueness.
            MatchIsUnique = config.Kind is BulkOperationKind.Merge || matchColumns.Any(column => column.IsKey)
        };
    }

    private static IReadOnlyList<ColumnMetadata> SelectBaseColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata
    ) where TEntity : class
    {
        var columns = config.ColumnSelectors.Count > 0
            ? Distinct(Parse(metadata, config.ColumnSelectors))
            : metadata.Columns;

        return config.ColumnMappings.Count > 0
            ? ApplyMappings(columns, config.ColumnMappings, metadata)
            : columns;
    }

    private static ColumnMetadata? ResolveIdentityOverride<TEntity>(
        BulkConfiguration<TEntity> config,
        IReadOnlyList<ColumnMetadata> baseColumns
    ) where TEntity : class
    {
        if (config.IdentityColumnOverride is null)
        {
            return null;
        }

        var propertyNames = PropertySelectorParser.ParsePropertyNames(config.IdentityColumnOverride);

        return baseColumns.FirstOrDefault(column => column.PropertyName == propertyNames[0]);
    }

    /// <summary>
    /// A plain insert matches nothing: giving it the key columns would put a primary key on the
    /// staging table over identity values the database has not generated yet.
    /// </summary>
    private static IReadOnlyList<ColumnMetadata> ResolveMatchColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMetadata> baseColumns
    ) where TEntity : class
    {
        if (config.MatchSelectors.Count > 0)
        {
            return Rebase(Parse(metadata, config.MatchSelectors), baseColumns);
        }

        if (config.InsertIfMissingSelectors.Count > 0)
        {
            return Rebase(Parse(metadata, config.InsertIfMissingSelectors), baseColumns);
        }

        var needsKeys = config.Kind is not BulkOperationKind.Insert || config.InsertIfMissing;

        return needsKeys
            ? Rebase(metadata.KeyColumns, baseColumns)
            : [];
    }

    private static IReadOnlyList<ColumnMetadata> ResolveInsertColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMetadata> baseColumns,
        ColumnMetadata? identityOverride
    ) where TEntity : class
    {
        if (config.InsertColumnSelectors.Count > 0)
        {
            return
            [
                .. Rebase(Parse(metadata, config.InsertColumnSelectors), baseColumns)
                    .Where(IsWritable)
            ];
        }

        return
        [
            .. baseColumns
                .Where(IsWritable)
                .Where(column => config.KeepIdentity || !IsIdentity(column, identityOverride))
        ];
    }

    private static IReadOnlyList<ColumnMetadata> ResolveUpdateColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMetadata> baseColumns,
        ColumnMetadata? identityOverride
    ) where TEntity : class
    {
        if (config.UpdateColumnSelectors.Count > 0)
        {
            return
            [
                .. Rebase(Parse(metadata, config.UpdateColumnSelectors), baseColumns)
                    .Where(column => IsWritable(column) && !column.IsKey)
            ];
        }

        return
        [
            .. baseColumns
                .Where(column => IsWritable(column) && !column.IsKey && !column.IsConcurrencyToken)
                .Where(column => !IsIdentity(column, identityOverride))
        ];
    }

    /// <summary>
    /// An insert has no existing row to check against, and a token the caller left out of
    /// WithColumns is not available to compare either.
    /// </summary>
    private static IReadOnlyList<ColumnMetadata> ResolveConcurrencyColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMetadata> baseColumns
    ) where TEntity : class
    {
        if (config.Kind is BulkOperationKind.Insert || metadata.ConcurrencyColumn is null)
        {
            return [];
        }

        var useConcurrency = config.ConcurrencyCheckOverride ?? true;
        var token = baseColumns.FirstOrDefault(column =>
            column.PropertyName == metadata.ConcurrencyColumn.PropertyName);

        return useConcurrency && token is not null
            ? [token]
            : [];
    }

    private static IReadOnlyList<ColumnMetadata> ResolveOutputColumns<TEntity>(
        BulkConfiguration<TEntity> config,
        EntityMetadata metadata,
        IReadOnlyList<ColumnMetadata> baseColumns
    ) where TEntity : class
    {
        var outputColumns = new List<ColumnMetadata>();

        if (config.OutputIdentity)
        {
            outputColumns.AddRange(baseColumns.Where(column => column.IsStoreGenerated));
        }

        if (config.OutputSelectors.Count > 0)
        {
            outputColumns.AddRange(Rebase(Parse(metadata, config.OutputSelectors), baseColumns));
        }

        return Distinct(outputColumns);
    }

    /// <summary>The union of everything the generated statement reads out of the staging table.</summary>
    private static IReadOnlyList<ColumnMetadata> ResolveStagingColumns(
        BulkOperationKind kind,
        IReadOnlyList<ColumnMetadata> matchColumns,
        IReadOnlyList<ColumnMetadata> insertColumns,
        IReadOnlyList<ColumnMetadata> updateColumns,
        IReadOnlyList<ColumnMetadata> concurrencyColumns
    )
    {
        IEnumerable<ColumnMetadata> needed = kind switch
        {
            BulkOperationKind.Delete => [.. matchColumns, .. concurrencyColumns],
            BulkOperationKind.Update => [.. matchColumns, .. updateColumns, .. concurrencyColumns],
            BulkOperationKind.Merge => [.. matchColumns, .. insertColumns, .. updateColumns, .. concurrencyColumns],
            _ => [.. matchColumns, .. insertColumns, .. concurrencyColumns]
        };

        return DistinctByColumnName(needed);
    }

    private static IEnumerable<ColumnMetadata> Parse<TEntity>(
        EntityMetadata metadata,
        IEnumerable<Expression<Func<TEntity, object?>>> selectors
    ) where TEntity : class
        => selectors.SelectMany(selector => PropertySelectorParser.Parse(metadata, selector));

    /// <summary>Re-points columns resolved against the raw metadata onto their (possibly remapped) whitelist twins.</summary>
    private static IReadOnlyList<ColumnMetadata> Rebase(
        IEnumerable<ColumnMetadata> columns,
        IReadOnlyList<ColumnMetadata> baseColumns
    ) => [.. columns.Select(column => Rebase(column, baseColumns))];

    private static ColumnMetadata Rebase(ColumnMetadata column, IReadOnlyList<ColumnMetadata> baseColumns)
        => baseColumns.FirstOrDefault(candidate => candidate.PropertyName == column.PropertyName) ?? column;

    private static bool IsWritable(ColumnMetadata column)
        => !column.IsComputed && !column.IsRowVersion;

    private static bool IsIdentity(ColumnMetadata column, ColumnMetadata? identityOverride)
        => column.IsIdentity
            || identityOverride is not null && column.PropertyName == identityOverride.PropertyName;

    private static IReadOnlyList<ColumnMetadata> Distinct(IEnumerable<ColumnMetadata> columns)
        => [.. columns.DistinctBy(column => column.PropertyName, StringComparer.Ordinal)];

    private static IReadOnlyList<ColumnMetadata> DistinctByColumnName(IEnumerable<ColumnMetadata> columns)
        => [.. columns.DistinctBy(column => column.ColumnName, StringComparer.Ordinal)];

    private static IReadOnlyList<ColumnMetadata> ApplyMappings(
        IReadOnlyList<ColumnMetadata> columns,
        IReadOnlyDictionary<string, string> mappings,
        EntityMetadata metadata
    )
    {
        foreach (var mapping in mappings)
        {
            if (metadata.FindByPropertyName(mapping.Key) is null)
            {
                var error = new BulkConfigurationException(
                    message: $"Column mapping references unknown property '{mapping.Key}' on '{metadata.ClrType.Name}'."
                );

                throw error;
            }
        }

        return
        [
            .. columns.Select(column => mappings.TryGetValue(column.PropertyName, out var mappedName)
                ? column with { ColumnName = mappedName }
                : column)
        ];
    }
}
