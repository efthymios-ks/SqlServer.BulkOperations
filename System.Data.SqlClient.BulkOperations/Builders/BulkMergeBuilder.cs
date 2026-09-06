using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Configuration;

namespace System.Data.SqlClient.BulkOperations.Builders;

internal sealed class BulkMergeBuilder<TEntity>(BulkConfiguration<TEntity> config)
    : BulkBuilderBase<TEntity, IBulkMergeBuilder<TEntity>>(config), IBulkMergeBuilder<TEntity>
    where TEntity : class
{
    protected override IBulkMergeBuilder<TEntity> Self
        => this;

    public IBulkMergeBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector)
    {
        Config.MatchSelectors.Add(selector);

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithInsertColumns(Expression<Func<TEntity, object?>> selector)
    {
        Config.InsertColumnSelectors.Add(selector);

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithUpdateColumns(Expression<Func<TEntity, object?>> selector)
    {
        Config.UpdateColumnSelectors.Add(selector);

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithDeleteWhenNotMatched()
    {
        Config.DeleteWhenNotMatched = true;

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithDuplicateKeys(DuplicateKeyBehavior behavior)
    {
        Config.DuplicateKeys = behavior;

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = true;

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithoutConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = false;

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithThrowOnConcurrencyMismatch()
    {
        Config.ThrowOnConcurrencyMismatch = true;

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector)
    {
        Config.OutputSelectors.Add(selector);

        return this;
    }

    public IBulkMergeBuilder<TEntity> WithOutputIdentity()
    {
        Config.OutputIdentity = true;

        return this;
    }
}
