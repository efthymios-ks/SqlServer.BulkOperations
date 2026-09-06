namespace System.Data.SqlClient.BulkOperations.Exceptions;

/// <summary>Fewer rows changed than were sent, so some target rows had moved on.</summary>
public sealed class BulkConcurrencyException(int expected, int actual)
    : BulkOperationException(
        message: $"Concurrency check failed: expected {expected} affected rows but got {actual}."
    )
{
    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}
