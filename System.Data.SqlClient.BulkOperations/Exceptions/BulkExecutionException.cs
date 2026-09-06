namespace System.Data.SqlClient.BulkOperations.Exceptions;

/// <summary>A SQL Server error that survived the retry policy.</summary>
public sealed class BulkExecutionException(string message, Exception inner)
    : BulkOperationException(message, inner);
