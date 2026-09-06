using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Configuration;

namespace System.Data.SqlClient.BulkOperations.Builders;

internal sealed class BulkUpdateBuilder<TEntity>(BulkConfiguration<TEntity> config)
    : BulkBuilderBase<TEntity, IBulkUpdateBuilder<TEntity>>(config), IBulkUpdateBuilder<TEntity>
    where TEntity : class
{
    protected override IBulkUpdateBuilder<TEntity> Self
        => this;

    public IBulkUpdateBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector)
    {
        Config.MatchSelectors.Add(selector);

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithIdentityColumn(Expression<Func<TEntity, object?>> selector)
    {
        Config.IdentityColumnOverride = selector;

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithUpdateColumns(Expression<Func<TEntity, object?>> selector)
    {
        Config.UpdateColumnSelectors.Add(selector);

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = true;

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithoutConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = false;

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithThrowOnConcurrencyMismatch()
    {
        Config.ThrowOnConcurrencyMismatch = true;

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithRequireAllMatched()
    {
        Config.RequireAllMatched = true;

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector)
    {
        Config.OutputSelectors.Add(selector);

        return this;
    }

    public IBulkUpdateBuilder<TEntity> WithOutputIdentity()
    {
        Config.OutputIdentity = true;

        return this;
    }
}
