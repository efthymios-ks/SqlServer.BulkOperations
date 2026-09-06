namespace System.Data.SqlClient.BulkOperations;

/// <summary>What a merge does when two source items carry the same match key.</summary>
public enum DuplicateKeyBehavior
{
    /// <summary>Keep the last item for each key and drop the earlier ones.</summary>
    Deduplicate = 1,

    /// <summary>Reject the whole batch with a <see cref="Exceptions.BulkConfigurationException"/>.</summary>
    Throw = 2
}
