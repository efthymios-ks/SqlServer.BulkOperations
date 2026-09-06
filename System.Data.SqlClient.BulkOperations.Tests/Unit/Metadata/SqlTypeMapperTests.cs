using System.Data;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Metadata;

public class SqlTypeMapperTests
{
    [Theory]
    [InlineData(typeof(string), SqlDbType.NVarChar)]
    [InlineData(typeof(char), SqlDbType.NChar)]
    [InlineData(typeof(byte[]), SqlDbType.VarBinary)]
    [InlineData(typeof(Guid), SqlDbType.UniqueIdentifier)]
    [InlineData(typeof(bool), SqlDbType.Bit)]
    [InlineData(typeof(byte), SqlDbType.TinyInt)]
    [InlineData(typeof(sbyte), SqlDbType.SmallInt)]
    [InlineData(typeof(short), SqlDbType.SmallInt)]
    [InlineData(typeof(ushort), SqlDbType.Int)]
    [InlineData(typeof(int), SqlDbType.Int)]
    [InlineData(typeof(uint), SqlDbType.BigInt)]
    [InlineData(typeof(long), SqlDbType.BigInt)]
    [InlineData(typeof(ulong), SqlDbType.Decimal)]
    [InlineData(typeof(float), SqlDbType.Real)]
    [InlineData(typeof(double), SqlDbType.Float)]
    [InlineData(typeof(decimal), SqlDbType.Decimal)]
    [InlineData(typeof(DateTime), SqlDbType.DateTime2)]
    [InlineData(typeof(DateTimeOffset), SqlDbType.DateTimeOffset)]
    [InlineData(typeof(DateOnly), SqlDbType.Date)]
    [InlineData(typeof(TimeOnly), SqlDbType.Time)]
    [InlineData(typeof(TimeSpan), SqlDbType.Time)]
    public void Map_WhenGivenAClrType_ShouldPickTheMatchingSqlType(Type clrType, SqlDbType expected)
        => Assert.Equal(expected, SqlTypeMapper.Map(clrType));

    [Fact]
    public void Map_WhenTheTypeIsNullable_ShouldUseTheUnderlyingType()
        => Assert.Equal(SqlDbType.Int, SqlTypeMapper.Map(typeof(int?)));

    [Fact]
    public void Map_WhenTheTypeIsAnEnum_ShouldUseItsUnderlyingIntegralType()
    {
        // Act & Assert
        Assert.Equal(SqlDbType.Int, SqlTypeMapper.Map(typeof(WidgetStatus)));
        Assert.Equal(SqlDbType.TinyInt, SqlTypeMapper.Map(typeof(ByteBacked)));
    }

    [Fact]
    public void Map_WhenTheTypeIsANullableEnum_ShouldStillUseTheUnderlyingIntegralType()
        => Assert.Equal(SqlDbType.Int, SqlTypeMapper.Map(typeof(WidgetStatus?)));

    [Fact]
    public void Map_WhenTheTypeIsUnsupported_ShouldThrow()
        => Assert.Throws<NotSupportedException>(() => SqlTypeMapper.Map(typeof(Uri)));

    [Theory]
    [InlineData(SqlDbType.Bit, "bit")]
    [InlineData(SqlDbType.TinyInt, "tinyint")]
    [InlineData(SqlDbType.SmallInt, "smallint")]
    [InlineData(SqlDbType.Int, "int")]
    [InlineData(SqlDbType.BigInt, "bigint")]
    [InlineData(SqlDbType.Real, "real")]
    [InlineData(SqlDbType.Float, "float")]
    [InlineData(SqlDbType.Date, "date")]
    [InlineData(SqlDbType.UniqueIdentifier, "uniqueidentifier")]
    public void Declare_WhenTheTypeHasNoFacets_ShouldNotAddAny(SqlDbType sqlType, string expected)
        => Assert.Equal(expected, SqlTypeMapper.Declare(sqlType, null, null, null));

    [Theory]
    [InlineData(null, "nvarchar(max)")]
    [InlineData(0, "nvarchar(max)")]
    [InlineData(-1, "nvarchar(max)")]
    [InlineData(4001, "nvarchar(max)")]
    [InlineData(100, "nvarchar(100)")]
    [InlineData(4000, "nvarchar(4000)")]
    public void Declare_WhenNVarCharLengthIsOutsideTheInlineRange_ShouldUseMax(int? maxLength, string expected)
        => Assert.Equal(expected, SqlTypeMapper.Declare(SqlDbType.NVarChar, maxLength, null, null));

    [Fact]
    public void Declare_WhenTheTypeIsVarBinary_ShouldFollowTheSameLengthRule()
    {
        // Act & Assert
        Assert.Equal("varbinary(max)", SqlTypeMapper.Declare(SqlDbType.VarBinary, null, null, null));
        Assert.Equal("varbinary(64)", SqlTypeMapper.Declare(SqlDbType.VarBinary, 64, null, null));
    }

    [Fact]
    public void Declare_WhenAFixedWidthTypeIsUnsized_ShouldUseTheDefaultWidth()
    {
        // Act & Assert
        Assert.Equal("nchar(1)", SqlTypeMapper.Declare(SqlDbType.NChar, null, null, null));
        Assert.Equal("char(3)", SqlTypeMapper.Declare(SqlDbType.Char, 3, null, null));
        Assert.Equal("binary(8)", SqlTypeMapper.Declare(SqlDbType.Binary, null, null, null));
    }

    [Fact]
    public void Declare_WhenTheTypeIsDecimal_ShouldUseTheGivenPrecisionAndScaleOrTheDefault()
    {
        // Act & Assert
        Assert.Equal("decimal(18,2)", SqlTypeMapper.Declare(SqlDbType.Decimal, null, null, null));
        Assert.Equal("decimal(28,6)", SqlTypeMapper.Declare(SqlDbType.Decimal, null, 28, 6));
    }

    [Fact]
    public void Declare_WhenATemporalTypeHasNoScale_ShouldUseTheFullFractionalScale()
    {
        // Act & Assert
        Assert.Equal("datetime2(7)", SqlTypeMapper.Declare(SqlDbType.DateTime2, null, null, null));
        Assert.Equal("datetimeoffset(7)", SqlTypeMapper.Declare(SqlDbType.DateTimeOffset, null, null, null));
        Assert.Equal("time(3)", SqlTypeMapper.Declare(SqlDbType.Time, null, null, 3));
    }

    [Fact]
    public void FromStoreValue_WhenTheValueIsNullOrDbNull_ShouldReturnNull()
    {
        // Act & Assert
        Assert.Null(SqlTypeMapper.FromStoreValue(null, typeof(int)));
        Assert.Null(SqlTypeMapper.FromStoreValue(DBNull.Value, typeof(string)));
    }

    [Fact]
    public void FromStoreValue_WhenTheValueAlreadyFits_ShouldReturnItUnchanged()
    {
        // Arrange & Act
        var bytes = new byte[] { 1, 2, 3 };

        // Assert
        Assert.Same(bytes, SqlTypeMapper.FromStoreValue(bytes, typeof(byte[])));
        Assert.Equal(42, SqlTypeMapper.FromStoreValue(42, typeof(int)));
    }

    [Fact]
    public void FromStoreValue_WhenTheTargetIsNullable_ShouldUnwrapAndConvert()
        => Assert.Equal(42, SqlTypeMapper.FromStoreValue(42L, typeof(int?)));

    [Fact]
    public void FromStoreValue_WhenTheTargetIsAnEnum_ShouldConvertFromTheIntegralValue()
        => Assert.Equal(WidgetStatus.Retired, SqlTypeMapper.FromStoreValue(2, typeof(WidgetStatus)));

    [Fact]
    public void FromStoreValue_WhenTheValueNeedsWidening_ShouldConvertInvariantly()
    {
        // Act & Assert
        Assert.Equal(1.5m, SqlTypeMapper.FromStoreValue(1.5d, typeof(decimal)));
        Assert.Equal(7L, SqlTypeMapper.FromStoreValue(7, typeof(long)));
    }

    private enum ByteBacked : byte
    {
        One = 1
    }
}
