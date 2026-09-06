using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Execution;

/// <summary>
/// Streams a list of entities to <see cref="Microsoft.Data.SqlClient.SqlBulkCopy"/> without
/// materialising a DataTable, optionally prefixing each row with its source index.
/// </summary>
internal sealed class BulkDataReader<TEntity> : DbDataReader
{
    private readonly IReadOnlyList<TEntity> _items;
    private readonly IReadOnlyList<ColumnMetadata> _columns;
    private readonly Dictionary<string, int> _ordinalsByName;
    private readonly int _dataColumnOffset;
    private int _currentIndex = -1;

    public BulkDataReader(
        IReadOnlyList<TEntity> items,
        IReadOnlyList<ColumnMetadata> columns,
        bool withOrdinal
    )
    {
        _items = items;
        _columns = columns;
        _dataColumnOffset = withOrdinal ? 1 : 0;
        _ordinalsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (withOrdinal)
        {
            _ordinalsByName[BulkColumns.Ordinal] = 0;
        }

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            _ordinalsByName[columns[columnIndex].ColumnName] = columnIndex + _dataColumnOffset;
        }
    }

    public override int FieldCount
        => _columns.Count + _dataColumnOffset;

    public override bool HasRows
        => _items.Count > 0;

    public override int Depth
        => 0;

    public override bool IsClosed
        => false;

    public override int RecordsAffected
        => -1;

    public override object this[int i]
        => GetValue(i);

    public override object this[string name]
        => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        _currentIndex++;

        return _currentIndex < _items.Count;
    }

    public override bool NextResult()
        => false;

    public override int GetOrdinal(string name)
        => _ordinalsByName.TryGetValue(name, out var ordinal)
            ? ordinal
            : throw new IndexOutOfRangeException(name);

    public override string GetName(int i)
        => IsOrdinalColumn(i)
            ? BulkColumns.Ordinal
            : ColumnAt(i).ColumnName;

    public override Type GetFieldType(int i)
        => IsOrdinalColumn(i)
            ? typeof(int)
            : StoreType(ColumnAt(i));

    public override object GetValue(int i)
    {
        if (IsOrdinalColumn(i))
        {
            return _currentIndex;
        }

        var column = ColumnAt(i);
        var value = column.Getter(_items[_currentIndex]!);

        if (value is null)
        {
            return DBNull.Value;
        }

        // SqlBulkCopy has no notion of enums, so they travel as their underlying integral value.
        var valueType = value.GetType();

        return valueType.IsEnum
            ? Convert.ChangeType(value, Enum.GetUnderlyingType(valueType), CultureInfo.InvariantCulture)
            : value;
    }

    public override bool IsDBNull(int i)
        => GetValue(i) is DBNull;

    public override int GetValues(object[] values)
    {
        var copyCount = Math.Min(values.Length, FieldCount);

        for (var columnIndex = 0; columnIndex < copyCount; columnIndex++)
        {
            values[columnIndex] = GetValue(columnIndex);
        }

        return copyCount;
    }

    public override IEnumerator GetEnumerator()
        => new DbEnumerator(this);

    public override string GetDataTypeName(int i)
        => GetFieldType(i).Name;

    public override bool GetBoolean(int i)
        => (bool)GetValue(i);

    public override byte GetByte(int i)
        => (byte)GetValue(i);

    public override char GetChar(int i)
        => (char)GetValue(i);

    public override DateTime GetDateTime(int i)
        => (DateTime)GetValue(i);

    public override decimal GetDecimal(int i)
        => (decimal)GetValue(i);

    public override double GetDouble(int i)
        => (double)GetValue(i);

    public override float GetFloat(int i)
        => (float)GetValue(i);

    public override Guid GetGuid(int i)
        => (Guid)GetValue(i);

    public override short GetInt16(int i)
        => (short)GetValue(i);

    public override int GetInt32(int i)
        => (int)GetValue(i);

    public override long GetInt64(int i)
        => (long)GetValue(i);

    public override string GetString(int i)
        => (string)GetValue(i);

    public override long GetBytes(int i, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override long GetChars(int i, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    private bool IsOrdinalColumn(int i)
        => _dataColumnOffset == 1 && i == 0;

    private ColumnMetadata ColumnAt(int i)
        => _columns[i - _dataColumnOffset];

    private static Type StoreType(ColumnMetadata column)
    {
        var propertyType = column.Property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return underlyingType.IsEnum
            ? Enum.GetUnderlyingType(underlyingType)
            : underlyingType;
    }
}
