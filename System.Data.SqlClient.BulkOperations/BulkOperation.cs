using System.Data.SqlClient.BulkOperations.Builders;
using System.Data.SqlClient.BulkOperations.Configuration;

namespace System.Data.SqlClient.BulkOperations;

public static class BulkOperation
{
    public static IBulkInsertBuilder<TEntity> Insert<TEntity>(IReadOnlyList<TEntity> items) where TEntity : class
        => new BulkInsertBuilder<TEntity>(Configure(BulkOperationKind.Insert, items));

    public static IBulkUpdateBuilder<TEntity> Update<TEntity>(IReadOnlyList<TEntity> items) where TEntity : class
        => new BulkUpdateBuilder<TEntity>(Configure(BulkOperationKind.Update, items));

    public static IBulkDeleteBuilder<TEntity> Delete<TEntity>(IReadOnlyList<TEntity> items) where TEntity : class
        => new BulkDeleteBuilder<TEntity>(Configure(BulkOperationKind.Delete, items));

    public static IBulkMergeBuilder<TEntity> Merge<TEntity>(IReadOnlyList<TEntity> items) where TEntity : class
        => new BulkMergeBuilder<TEntity>(Configure(BulkOperationKind.Merge, items));

    private static BulkConfiguration<TEntity> Configure<TEntity>(BulkOperationKind kind, IReadOnlyList<TEntity> items) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(items);

        return new BulkConfiguration<TEntity>(kind, items);
    }
}
