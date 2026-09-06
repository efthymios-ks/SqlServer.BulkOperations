using System.Linq.Expressions;

namespace System.Data.SqlClient.BulkOperations.Builders;

public interface IBulkUpdateBuilder<TEntity>
    : IBulkBuilder<TEntity, IBulkUpdateBuilder<TEntity>>
    where TEntity : class
{
    /// <summary>Columns to join target rows on. Defaults to the key columns.</summary>
    IBulkUpdateBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector);

    /// <summary>Treats this property as the identity column, excluding it from the SET list.</summary>
    IBulkUpdateBuilder<TEntity> WithIdentityColumn(Expression<Func<TEntity, object?>> selector);

    /// <summary>Columns to write. Defaults to everything that is not a key, identity, computed or token column.</summary>
    IBulkUpdateBuilder<TEntity> WithUpdateColumns(Expression<Func<TEntity, object?>> selector);

    /// <summary>Also requires the concurrency token to match. On by default when the entity has one.</summary>
    IBulkUpdateBuilder<TEntity> WithConcurrencyCheck();

    /// <inheritdoc cref="WithConcurrencyCheck"/>
    IBulkUpdateBuilder<TEntity> WithoutConcurrencyCheck();

    /// <summary>Throws <see cref="Exceptions.BulkConcurrencyException"/> when fewer rows changed than were sent.</summary>
    IBulkUpdateBuilder<TEntity> WithThrowOnConcurrencyMismatch();

    /// <summary>Throws <see cref="Exceptions.BulkNotMatchedException"/> when an item matched no row.</summary>
    IBulkUpdateBuilder<TEntity> WithRequireAllMatched();

    /// <summary>Reads these columns back onto the source items after the update.</summary>
    IBulkUpdateBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector);

    /// <summary>Refreshes every store-generated column — notably the concurrency token — on the source items.</summary>
    IBulkUpdateBuilder<TEntity> WithOutputIdentity();
}
