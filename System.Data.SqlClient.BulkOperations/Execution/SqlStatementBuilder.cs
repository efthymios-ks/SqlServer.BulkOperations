using System.Text;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal sealed record BuiltStatement(string Sql, bool ReturnsRows);

internal static class SqlStatementBuilder
{
    /// <summary>
    /// Inserts every staged row and reads the store-generated values back.
    /// A plain INSERT cannot do this: its OUTPUT clause may only reference INSERTED, so there is no
    /// way to say which source item a returned row came from. A MERGE that never matches can,
    /// because its OUTPUT clause can reach into the source.
    /// </summary>
    public static BuiltStatement BuildInsertWithOutput(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> insertColumns,
        IReadOnlyList<ColumnMetadata> outputColumns
    ) => BuildInsertViaMerge(
        targetQualified: targetQualified,
        tempTable: tempTable,
        insertColumns: insertColumns,
        outputColumns: outputColumns,
        matchPredicate: NeverMatches
    );

    public static BuiltStatement BuildInsertIfMissing(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> insertColumns,
        IReadOnlyList<ColumnMetadata> matchColumns,
        IReadOnlyList<ColumnMetadata> outputColumns
    )
    {
        if (outputColumns.Count > 0)
        {
            return BuildInsertViaMerge(
                targetQualified: targetQualified,
                tempTable: tempTable,
                insertColumns: insertColumns,
                outputColumns: outputColumns,
                matchPredicate: MatchPredicate(matchColumns)
            );
        }

        // Nothing to read back, so the cheaper anti-semi-join beats a MERGE.
        var builder = new StringBuilder();
        builder.Append("INSERT INTO ")
               .Append(targetQualified)
               .Append(" (")
               .Append(JoinColumns(insertColumns))
               .AppendLine(")")
               .Append("SELECT ")
               .AppendLine(JoinColumns(insertColumns, prefix: "src."))
               .Append("FROM ")
               .Append(SqlIdentifier.Quote(tempTable))
               .AppendLine(" AS src")
               .Append("WHERE NOT EXISTS (SELECT 1 FROM ")
               .Append(targetQualified)
               .Append(" AS tgt WHERE ")
               .Append(MatchPredicate(matchColumns))
               .AppendLine(");");

        return new BuiltStatement(builder.ToString(), ReturnsRows: false);
    }

    private static BuiltStatement BuildInsertViaMerge(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> insertColumns,
        IReadOnlyList<ColumnMetadata> outputColumns,
        string matchPredicate
    )
    {
        var builder = new StringBuilder();
        builder.Append("MERGE INTO ")
               .Append(targetQualified)
               .AppendLine(" WITH (HOLDLOCK) AS tgt")
               .Append("USING ")
               .Append(SqlIdentifier.Quote(tempTable))
               .AppendLine(" AS src")
               .Append("    ON ")
               .AppendLine(matchPredicate)
               .AppendLine("WHEN NOT MATCHED BY TARGET THEN")
               .Append("    INSERT (")
               .Append(JoinColumns(insertColumns))
               .Append(") VALUES (")
               .Append(JoinColumns(insertColumns, prefix: "src."))
               .AppendLine(")");

        AppendOutputClause(builder, outputColumns);
        builder.AppendLine(";");

        return new BuiltStatement(builder.ToString(), ReturnsRows: true);
    }

    public static BuiltStatement BuildUpdate(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> updateColumns,
        IReadOnlyList<ColumnMetadata> matchColumns,
        IReadOnlyList<ColumnMetadata> concurrencyColumns,
        IReadOnlyList<ColumnMetadata> outputColumns
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("UPDATE tgt")
               .Append("SET ")
               .AppendLine(Assignments(updateColumns));

        if (outputColumns.Count > 0)
        {
            AppendOutputClause(builder, outputColumns);
        }

        builder.Append("FROM ")
               .Append(targetQualified)
               .AppendLine(" AS tgt")
               .Append("INNER JOIN ")
               .Append(SqlIdentifier.Quote(tempTable))
               .Append(" AS src ON ")
               .AppendLine(MatchPredicate(matchColumns));

        if (concurrencyColumns.Count > 0)
        {
            builder.Append("WHERE ")
                   .AppendLine(MatchPredicate(concurrencyColumns));
        }

        builder.AppendLine(";");

        return new BuiltStatement(builder.ToString(), ReturnsRows: outputColumns.Count > 0);
    }

    public static BuiltStatement BuildDelete(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> matchColumns,
        IReadOnlyList<ColumnMetadata> concurrencyColumns
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("DELETE tgt")
               .Append("FROM ")
               .Append(targetQualified)
               .AppendLine(" AS tgt")
               .Append("INNER JOIN ")
               .Append(SqlIdentifier.Quote(tempTable))
               .Append(" AS src ON ")
               .AppendLine(MatchPredicate(matchColumns));

        if (concurrencyColumns.Count > 0)
        {
            builder.Append("WHERE ")
                   .AppendLine(MatchPredicate(concurrencyColumns));
        }

        builder.AppendLine(";");

        return new BuiltStatement(builder.ToString(), ReturnsRows: false);
    }

    public static BuiltStatement BuildMerge(
        string targetQualified,
        string tempTable,
        IReadOnlyList<ColumnMetadata> insertColumns,
        IReadOnlyList<ColumnMetadata> updateColumns,
        IReadOnlyList<ColumnMetadata> matchColumns,
        IReadOnlyList<ColumnMetadata> concurrencyColumns,
        IReadOnlyList<ColumnMetadata> outputColumns,
        bool deleteWhenNotMatched
    )
    {
        var builder = new StringBuilder();

        // HOLDLOCK closes the race where a concurrent session inserts a key between the
        // match probe and the insert, which would surface as a duplicate-key violation.
        builder.Append("MERGE INTO ")
               .Append(targetQualified)
               .AppendLine(" WITH (HOLDLOCK) AS tgt")
               .Append("USING ")
               .Append(SqlIdentifier.Quote(tempTable))
               .AppendLine(" AS src")
               .Append("    ON ")
               .AppendLine(MatchPredicate(matchColumns));

        if (updateColumns.Count > 0)
        {
            builder.Append("WHEN MATCHED");

            if (concurrencyColumns.Count > 0)
            {
                builder.Append(" AND ").Append(MatchPredicate(concurrencyColumns));
            }

            builder.AppendLine(" THEN")
                   .Append("    UPDATE SET ")
                   .AppendLine(Assignments(updateColumns));
        }

        builder.AppendLine("WHEN NOT MATCHED BY TARGET THEN")
               .Append("    INSERT (")
               .Append(JoinColumns(insertColumns))
               .Append(") VALUES (")
               .Append(JoinColumns(insertColumns, prefix: "src."))
               .AppendLine(")");

        if (deleteWhenNotMatched)
        {
            builder.AppendLine("WHEN NOT MATCHED BY SOURCE THEN DELETE");
        }

        // $action tells inserts, updates and deletes apart in a single result set.
        builder.Append("OUTPUT $action");

        foreach (var column in outputColumns)
        {
            builder.Append(", INSERTED.").Append(SqlIdentifier.Quote(column.ColumnName));
        }

        builder.Append(", ")
               .AppendLine(SourceOrdinal)
               .AppendLine(";");

        return new BuiltStatement(builder.ToString(), ReturnsRows: true);
    }

    /// <summary>Emits <c>OUTPUT INSERTED.[..], src.[__bulk_index]</c> so results can be mapped back to source items.</summary>
    private static void AppendOutputClause(StringBuilder builder, IReadOnlyList<ColumnMetadata> outputColumns)
    {
        builder.Append("OUTPUT ");

        foreach (var column in outputColumns)
        {
            builder.Append("INSERTED.")
                   .Append(SqlIdentifier.Quote(column.ColumnName))
                   .Append(", ");
        }

        builder.AppendLine(SourceOrdinal);
    }

    private static string SourceOrdinal
        => "src." + SqlIdentifier.Quote(BulkColumns.Ordinal);

    /// <summary>A predicate no row can satisfy, which turns a MERGE into an insert-only statement.</summary>
    private const string NeverMatches = "1 = 0";

    private static string JoinColumns(IReadOnlyList<ColumnMetadata> columns, string prefix = "")
        => string.Join(", ", columns.Select(column => prefix + SqlIdentifier.Quote(column.ColumnName)));

    private static string Assignments(IReadOnlyList<ColumnMetadata> columns)
        => string.Join(", ", columns.Select(Equality));

    private static string MatchPredicate(IReadOnlyList<ColumnMetadata> columns)
        => string.Join(" AND ", columns.Select(Equality));

    private static string Equality(ColumnMetadata column)
    {
        var quoted = SqlIdentifier.Quote(column.ColumnName);

        return $"tgt.{quoted} = src.{quoted}";
    }
}
