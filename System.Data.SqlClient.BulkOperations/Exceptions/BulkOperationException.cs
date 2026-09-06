namespace System.Data.SqlClient.BulkOperations.Exceptions;

public abstract class BulkOperationException : Exception
{
    protected BulkOperationException(string message)
        : base(message)
    {
    }

    protected BulkOperationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
