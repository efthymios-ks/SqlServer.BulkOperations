using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Configuration;

namespace System.Data.SqlClient.BulkOperations.Builders;

internal sealed class BulkInsertBuilder<TEntity>(BulkConfiguration<TEntity> config)
    : BulkBuilderBase<TEntity, IBulkInsertBuilder<TEntity>>(config), IBulkInsertBuilder<TEntity>
    where TEntity : class
{
    protected override IBulkInsertBuilder<TEntity> Self
        => this;

    public IBulkInsertBuilder<TEntity> WithIdentityColumn(Expression<Func<TEntity, object?>> selector)
    {
        Config.IdentityColumnOverride = selector;

        return this;
    }

    public IBulkInsertBuilder<TEntity> WithKeepIdentity()
    {
        Config.KeepIdentity = true;

        return this;
    }

    public IBulkInsertBuilder<TEntity> WithInsertIfMissing(Expression<Func<TEntity, object?>>? selector = null)
    {
        Config.InsertIfMissing = true;

        if (selector is not null)
        {
            Config.InsertIfMissingSelectors.Add(selector);
        }

        return this;
    }

    public IBulkInsertBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector)
    {
        Config.OutputSelectors.Add(selector);

        return this;
    }

    public IBulkInsertBuilder<TEntity> WithOutputIdentity()
    {
        Config.OutputIdentity = true;

        return this;
    }
}
