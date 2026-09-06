namespace System.Data.SqlClient.BulkOperations.Exceptions;

/// <summary>Raised by WithRequireAllMatched when some items had no row to update.</summary>
public sealed class BulkNotMatchedException(int expected, int actual)
    : BulkOperationException(
        message: $"WithRequireAllMatched: expected {expected} matched rows but only {actual} were updated."
    )
{
    public int Expected { get; } = expected;

    public int Actual { get; } = actual;
}
