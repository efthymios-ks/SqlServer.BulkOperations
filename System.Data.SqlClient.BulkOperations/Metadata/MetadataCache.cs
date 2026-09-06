using System.Collections.Concurrent;

namespace System.Data.SqlClient.BulkOperations.Metadata;

/// <summary>Entity metadata is reflection-heavy to build and never changes, so it is built once per type.</summary>
internal static class MetadataCache
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> _cache = new();

    public static EntityMetadata For<TEntity>()
        => For(typeof(TEntity));

    public static EntityMetadata For(Type type)
        => _cache.GetOrAdd(type, EntityMetadataFactory.Build);

    public static void Clear()
        => _cache.Clear();
}
