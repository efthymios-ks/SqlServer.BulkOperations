using System.Linq.Expressions;

namespace System.Data.SqlClient.BulkOperations.Builders;

public interface IBulkDeleteBuilder<TEntity>
    : IBulkBuilder<TEntity, IBulkDeleteBuilder<TEntity>>
    where TEntity : class
{
    /// <summary>Columns to join target rows on. Defaults to the key columns.</summary>
    IBulkDeleteBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector);

    /// <summary>Also requires the concurrency token to match. On by default when the entity has one.</summary>
    IBulkDeleteBuilder<TEntity> WithConcurrencyCheck();

    /// <inheritdoc cref="WithConcurrencyCheck"/>
    IBulkDeleteBuilder<TEntity> WithoutConcurrencyCheck();

    /// <summary>Throws <see cref="Exceptions.BulkConcurrencyException"/> when fewer rows were deleted than sent.</summary>
    IBulkDeleteBuilder<TEntity> WithThrowOnConcurrencyMismatch();
}
