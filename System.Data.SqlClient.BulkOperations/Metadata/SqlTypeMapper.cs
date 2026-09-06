using System.Data;
using System.Globalization;

namespace System.Data.SqlClient.BulkOperations.Metadata;

internal static class SqlTypeMapper
{
    /// <summary>Above this an nvarchar/varbinary column has to be declared as (max).</summary>
    private const int MaxInlineLength = 4000;

    private const byte DefaultDecimalPrecision = 18;

    private const byte DefaultDecimalScale = 2;

    private const byte DefaultFractionalSecondsScale = 7;

    private static readonly Dictionary<Type, SqlDbType> _typeMap = new()
    {
        [typeof(string)] = SqlDbType.NVarChar,
        [typeof(char)] = SqlDbType.NChar,
        [typeof(char[])] = SqlDbType.NVarChar,
        [typeof(byte[])] = SqlDbType.VarBinary,
        [typeof(Guid)] = SqlDbType.UniqueIdentifier,
        [typeof(bool)] = SqlDbType.Bit,
        [typeof(byte)] = SqlDbType.TinyInt,

        // SQL Server has no signed byte or unsigned integral types, so each widens to the
        // smallest type that can hold its whole range.
        [typeof(sbyte)] = SqlDbType.SmallInt,
        [typeof(short)] = SqlDbType.SmallInt,
        [typeof(ushort)] = SqlDbType.Int,
        [typeof(int)] = SqlDbType.Int,
        [typeof(uint)] = SqlDbType.BigInt,
        [typeof(long)] = SqlDbType.BigInt,
        [typeof(ulong)] = SqlDbType.Decimal,
        [typeof(float)] = SqlDbType.Real,
        [typeof(double)] = SqlDbType.Float,
        [typeof(decimal)] = SqlDbType.Decimal,
        [typeof(DateTime)] = SqlDbType.DateTime2,
        [typeof(DateTimeOffset)] = SqlDbType.DateTimeOffset,
        [typeof(DateOnly)] = SqlDbType.Date,
        [typeof(TimeOnly)] = SqlDbType.Time,
        [typeof(TimeSpan)] = SqlDbType.Time
    };

    public static SqlDbType Map(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return _typeMap.TryGetValue(type, out var sqlDbType)
            ? sqlDbType
            : throw new NotSupportedException($"No SQL Server mapping for CLR type '{clrType.FullName}'.");
    }

    public static string Declare(SqlDbType sqlType, int? maxLength, byte? precision, byte? scale)
        => sqlType switch
        {
            SqlDbType.NVarChar => $"nvarchar({LengthOrMax(maxLength)})",
            SqlDbType.VarChar => $"varchar({LengthOrMax(maxLength)})",
            SqlDbType.VarBinary => $"varbinary({LengthOrMax(maxLength)})",
            SqlDbType.NChar => $"nchar({maxLength ?? 1})",
            SqlDbType.Char => $"char({maxLength ?? 1})",
            SqlDbType.Binary => $"binary({maxLength ?? 8})",
            SqlDbType.Decimal
                => $"decimal({precision ?? DefaultDecimalPrecision},{scale ?? DefaultDecimalScale})",
            SqlDbType.DateTime2 => $"datetime2({scale ?? DefaultFractionalSecondsScale})",
            SqlDbType.DateTimeOffset => $"datetimeoffset({scale ?? DefaultFractionalSecondsScale})",
            SqlDbType.Time => $"time({scale ?? DefaultFractionalSecondsScale})",
            SqlDbType.UniqueIdentifier => "uniqueidentifier",
            SqlDbType.Bit => "bit",
            SqlDbType.TinyInt => "tinyint",
            SqlDbType.SmallInt => "smallint",
            SqlDbType.Int => "int",
            SqlDbType.BigInt => "bigint",
            SqlDbType.Real => "real",
            SqlDbType.Float => "float",
            SqlDbType.Date => "date",
            _ => sqlType.ToString().ToLowerInvariant()
        };

    /// <summary>Converts a value read back from an OUTPUT clause into the property's CLR type.</summary>
    public static object? FromStoreValue(object? value, Type targetClrType)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        var targetType = Nullable.GetUnderlyingType(targetClrType) ?? targetClrType;

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        return targetType.IsEnum
            ? Enum.ToObject(targetType, value)
            : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static string LengthOrMax(int? maxLength)
        => maxLength is null or <= 0 or > MaxInlineLength
            ? "max"
            : maxLength.Value.ToString(CultureInfo.InvariantCulture);
}
