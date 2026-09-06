namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class BulkColumns
{
    /// <summary>
    /// Carries each item's position in the source list through the staging table so that
    /// OUTPUT rows can be matched back to the object they came from.
    /// </summary>
    public const string Ordinal = "__bulk_index";
}
