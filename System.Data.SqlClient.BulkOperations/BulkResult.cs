namespace System.Data.SqlClient.BulkOperations;

public sealed record BulkResult(
    int Inserted,
    int Updated,
    int Deleted,
    int TotalAffected,
    TimeSpan Elapsed,
    int Retries
)
{
    public static BulkResult Empty { get; } = new(
        Inserted: 0,
        Updated: 0,
        Deleted: 0,
        TotalAffected: 0,
        Elapsed: TimeSpan.Zero,
        Retries: 0
    );
}
