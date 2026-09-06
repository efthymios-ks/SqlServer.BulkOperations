namespace System.Data.SqlClient.BulkOperations.Exceptions;

/// <summary>The requested operation cannot be planned: a bad selector, an impossible option combination.</summary>
public sealed class BulkConfigurationException(string message)
    : BulkOperationException(message);
