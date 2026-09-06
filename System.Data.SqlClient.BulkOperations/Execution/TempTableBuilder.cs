using System.Text;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class TempTableBuilder
{
    public static string BuildCreateTable(
        string tempTableName,
        IReadOnlyList<ColumnMetadata> stagingColumns,
        IReadOnlyList<ColumnMetadata> matchColumns,
        bool includeOrdinal,
        bool matchIsUnique
    )
    {
        var matchColumnNames = matchColumns
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.Ordinal);

        var definitions = new List<string>(stagingColumns.Count + 2);

        if (includeOrdinal)
        {
            definitions.Add($"{SqlIdentifier.Quote(BulkColumns.Ordinal)} int NOT NULL");
        }

        foreach (var column in stagingColumns)
        {
            definitions.Add(DeclareColumn(column, isMatch: matchColumnNames.Contains(column.ColumnName)));
        }

        if (BuildClusteringKey(matchColumns, includeOrdinal, matchIsUnique) is { } clusteringKey)
        {
            definitions.Add(clusteringKey);
        }

        var builder = new StringBuilder();
        builder.Append("CREATE TABLE ")
               .Append(SqlIdentifier.Quote(tempTableName))
               .AppendLine(" (")
               .AppendJoin($",{Environment.NewLine}", definitions.Select(definition => "  " + definition))
               .AppendLine()
               .AppendLine(");");

        return builder.ToString();
    }

    private static string DeclareColumn(ColumnMetadata column, bool isMatch)
    {
        // Matching on a NULL never succeeds, so a match column may as well be NOT NULL and index better.
        var nullability = isMatch || !column.IsNullable
            ? "NOT NULL"
            : "NULL";

        // Without this, a staging column picks up the tempdb collation and joins against the
        // target column can fail with a collation conflict.
        var collation = column.IsCharacterType
            ? " COLLATE DATABASE_DEFAULT"
            : string.Empty;

        return $"{SqlIdentifier.Quote(column.ColumnName)} {column.SqlTypeDeclaration}{collation} {nullability}";
    }

    private static string? BuildClusteringKey(
        IReadOnlyList<ColumnMetadata> matchColumns,
        bool includeOrdinal,
        bool matchIsUnique
    )
    {
        if (matchColumns.Count == 0)
        {
            return includeOrdinal
                ? $"PRIMARY KEY CLUSTERED ({SqlIdentifier.Quote(BulkColumns.Ordinal)})"
                : null;
        }

        List<string> keyColumns = [.. matchColumns.Select(column => SqlIdentifier.Quote(column.ColumnName))];

        if (!matchIsUnique && includeOrdinal)
        {
            // Duplicate match keys are legal here, so the ordinal makes each staging row addressable.
            keyColumns.Add(SqlIdentifier.Quote(BulkColumns.Ordinal));
        }

        var clusterKind = matchIsUnique
            ? "PRIMARY KEY CLUSTERED"
            : "INDEX IX_bulk CLUSTERED";

        return $"{clusterKind} ({string.Join(", ", keyColumns)})";
    }
}
