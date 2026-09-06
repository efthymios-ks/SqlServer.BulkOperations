using Microsoft.Data.SqlClient;

namespace System.Data.SqlClient.BulkOperations.Execution;

internal static class RetryPolicy
{
    private static readonly HashSet<int> _transientErrorNumbers =
    [
        -2,      // command timeout
        1204,    // out of lock resources
        1205,    // deadlock victim
        1222,    // lock request timeout
        40501,   // Azure SQL service busy
        40613,   // Azure SQL database unavailable
        49918,   // Azure SQL cannot process request
        49919,   // Azure SQL too many create/update operations
        49920    // Azure SQL too many operations
    ];

    public static bool IsTransient(Exception exception)
        => exception switch
        {
            SqlException sqlException => sqlException.Errors
                .Cast<SqlError>()
                .Any(error => _transientErrorNumbers.Contains(error.Number)),
            TimeoutException => true,

            // SqlBulkCopy reports server errors wrapped in an InvalidOperationException, so a
            // transient deadlock during the copy would otherwise never be retried.
            { InnerException: { } inner } => IsTransient(inner),
            _ => false
        };

    public static SqlException? FindSqlException(Exception? exception)
        => exception switch
        {
            null => null,
            SqlException sqlException => sqlException,
            _ => FindSqlException(exception.InnerException)
        };

    /// <summary>
    /// Exponential backoff with full-width jitter, so a batch of clients retrying the same
    /// deadlock does not line up and collide again on the next attempt.
    /// </summary>
    public static TimeSpan Backoff(TimeSpan baseDelay, int attempt)
    {
        var delayMilliseconds = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitterMilliseconds = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(delayMilliseconds + jitterMilliseconds);
    }
}
