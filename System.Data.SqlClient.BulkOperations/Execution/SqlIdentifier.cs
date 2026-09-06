namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class SqlIdentifier
{
    /// <summary>Bracket-quotes an identifier, doubling any closing bracket so it cannot break out.</summary>
    public static string Quote(string identifier)
        => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public static string QualifiedName(string schema, string table)
        => Quote(schema) + "." + Quote(table);
}
