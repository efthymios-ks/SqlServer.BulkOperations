using System.Linq.Expressions;

namespace System.Data.SqlClient.BulkOperations.Builders;

public interface IBulkInsertBuilder<TEntity>
    : IBulkBuilder<TEntity, IBulkInsertBuilder<TEntity>>
    where TEntity : class
{
    /// <summary>Treats this property as the identity column when no [DatabaseGenerated] attribute says so.</summary>
    IBulkInsertBuilder<TEntity> WithIdentityColumn(Expression<Func<TEntity, object?>> selector);

    /// <summary>Inserts the identity values from the items instead of letting the database generate them.</summary>
    IBulkInsertBuilder<TEntity> WithKeepIdentity();

    /// <summary>Skips items whose match key already exists. Defaults to the key columns.</summary>
    IBulkInsertBuilder<TEntity> WithInsertIfMissing(Expression<Func<TEntity, object?>>? selector = null);

    /// <summary>Reads these columns back onto the source items after the insert.</summary>
    IBulkInsertBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector);

    /// <summary>Reads every store-generated column — identity, computed, rowversion — back onto the source items.</summary>
    IBulkInsertBuilder<TEntity> WithOutputIdentity();
}
