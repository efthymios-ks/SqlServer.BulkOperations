using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Configuration;

namespace System.Data.SqlClient.BulkOperations.Builders;

internal sealed class BulkDeleteBuilder<TEntity>(BulkConfiguration<TEntity> config)
    : BulkBuilderBase<TEntity, IBulkDeleteBuilder<TEntity>>(config), IBulkDeleteBuilder<TEntity>
    where TEntity : class
{
    protected override IBulkDeleteBuilder<TEntity> Self
        => this;

    public IBulkDeleteBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector)
    {
        Config.MatchSelectors.Add(selector);

        return this;
    }

    public IBulkDeleteBuilder<TEntity> WithConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = true;

        return this;
    }

    public IBulkDeleteBuilder<TEntity> WithoutConcurrencyCheck()
    {
        Config.ConcurrencyCheckOverride = false;

        return this;
    }

    public IBulkDeleteBuilder<TEntity> WithThrowOnConcurrencyMismatch()
    {
        Config.ThrowOnConcurrencyMismatch = true;

        return this;
    }
}
