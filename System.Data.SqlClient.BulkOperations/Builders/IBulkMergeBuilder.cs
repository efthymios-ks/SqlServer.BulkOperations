using System.Linq.Expressions;

namespace System.Data.SqlClient.BulkOperations.Builders;

public interface IBulkMergeBuilder<TEntity>
    : IBulkBuilder<TEntity, IBulkMergeBuilder<TEntity>>
    where TEntity : class
{
    /// <summary>Columns to match target rows on. Defaults to the key columns.</summary>
    IBulkMergeBuilder<TEntity> WithMatchOn(Expression<Func<TEntity, object?>> selector);

    /// <summary>Columns written by the WHEN NOT MATCHED insert.</summary>
    IBulkMergeBuilder<TEntity> WithInsertColumns(Expression<Func<TEntity, object?>> selector);

    /// <summary>Columns written by the WHEN MATCHED update. No update branch is emitted when empty.</summary>
    IBulkMergeBuilder<TEntity> WithUpdateColumns(Expression<Func<TEntity, object?>> selector);

    /// <summary>Deletes target rows absent from the source, making the table mirror the list.</summary>
    IBulkMergeBuilder<TEntity> WithDeleteWhenNotMatched();

    /// <summary>What to do when two items share a match key. Defaults to keeping the last.</summary>
    IBulkMergeBuilder<TEntity> WithDuplicateKeys(DuplicateKeyBehavior behavior);

    /// <summary>Also requires the concurrency token to match before updating. On by default when the entity has one.</summary>
    IBulkMergeBuilder<TEntity> WithConcurrencyCheck();

    /// <inheritdoc cref="WithConcurrencyCheck"/>
    IBulkMergeBuilder<TEntity> WithoutConcurrencyCheck();

    /// <summary>Throws <see cref="Exceptions.BulkConcurrencyException"/> when fewer rows changed than were sent.</summary>
    IBulkMergeBuilder<TEntity> WithThrowOnConcurrencyMismatch();

    /// <summary>Reads these columns back onto the source items after the merge.</summary>
    IBulkMergeBuilder<TEntity> WithOutput(Expression<Func<TEntity, object?>> selector);

    /// <summary>Reads every store-generated column back onto the source items.</summary>
    IBulkMergeBuilder<TEntity> WithOutputIdentity();
}
