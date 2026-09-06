using System.Data.SqlClient.BulkOperations.Execution;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Execution;

public class BulkDataReaderTests
{
    private readonly EntityMetadata _bookMetadata = MetadataCache.For<Book>();

    private readonly EntityMetadata _widgetMetadata = MetadataCache.For<Widget>();

    [Fact]
    public void Read_WhenIterating_ShouldWalkEveryItemExactlyOnceThenStop()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: true, count: 3, columns: "Isbn");

        var rowCount = 0;

        // Act
        while (reader.Read())
        {
            rowCount++;
        }

        // Assert
        Assert.Equal(3, rowCount);
        Assert.False(reader.Read());
    }

    [Fact]
    public void Read_WhenThereAreNoItems_ShouldReturnFalseImmediately()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: true, count: 0, columns: "Isbn");

        // Assert
        Assert.False(reader.Read());
        Assert.False(reader.HasRows);
    }

    [Fact]
    public void FieldCount_WhenTheOrdinalIsIncluded_ShouldCountItToo()
    {
        // Arrange & Act
        using var withOrdinal = BookReader(withOrdinal: true, count: 1, columns: ["Isbn", "Title"]);
        using var withoutOrdinal = BookReader(withOrdinal: false, count: 1, columns: ["Isbn", "Title"]);

        // Assert
        Assert.Equal(3, withOrdinal.FieldCount);
        Assert.Equal(2, withoutOrdinal.FieldCount);
    }

    [Fact]
    public void GetValue_WhenReadingTheOrdinalColumn_ShouldReturnTheRowIndex()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: true, count: 2, columns: "Isbn");

        reader.Read();
        Assert.Equal(0, reader.GetValue(0));

        // Act
        reader.Read();
        Assert.Equal(1, reader.GetValue(0));
    }

    [Fact]
    public void GetValue_WhenTheOrdinalIsOmitted_ShouldStartAtTheFirstDataColumn()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: false, count: 1, columns: "Isbn");

        // Act
        reader.Read();

        // Assert
        Assert.Equal("Isbn0001", reader.GetValue(0));
    }

    [Fact]
    public void GetValue_WhenThePropertyIsNull_ShouldReturnDbNull()
    {
        // Arrange
        var books = new[] { new Book { Isbn = "1", RowVersion = null } };
        using var reader = new BulkDataReader<Book>(books, Columns(_bookMetadata, "RowVersion"), withOrdinal: false);

        // Act
        reader.Read();

        // Assert
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public void GetValue_WhenThePropertyIsAnEnum_ShouldReturnItsUnderlyingIntegralValue()
    {
        // Arrange
        var widgets = new[] { new Widget { Name = "w", Status = WidgetStatus.Retired } };
        using var reader = new BulkDataReader<Widget>(widgets, Columns(_widgetMetadata, "Status"), withOrdinal: false);

        // Act
        reader.Read();
        var value = reader.GetValue(0);

        // Assert
        Assert.Equal((int)WidgetStatus.Retired, value);
        Assert.IsType<int>(value);
    }

    [Fact]
    public void GetFieldType_WhenTheOrdinalIsIncluded_ShouldReportIntThenTheUnwrappedColumnTypes()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: true, count: 1, columns: ["Isbn", "Price"]);

        // Assert
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.Equal(typeof(decimal), reader.GetFieldType(2));
    }

    [Fact]
    public void GetFieldType_WhenTheColumnIsAnEnum_ShouldReportItsUnderlyingType()
    {
        // Arrange & Act
        var widgets = Array.Empty<Widget>();
        using var reader = new BulkDataReader<Widget>(widgets, Columns(_widgetMetadata, "Status"), withOrdinal: false);

        // Assert
        Assert.Equal(typeof(int), reader.GetFieldType(0));
    }

    [Fact]
    public void GetFieldType_WhenTheColumnIsNullable_ShouldReportTheUnderlyingType()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: false, count: 0, columns: "RowVersion");

        // Assert
        Assert.Equal(typeof(byte[]), reader.GetFieldType(0));
    }

    [Fact]
    public void GetName_WhenTheOrdinalIsIncluded_ShouldReportItThenTheColumnNames()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: true, count: 1, columns: ["Isbn", "Title"]);

        // Assert
        Assert.Equal("__bulk_index", reader.GetName(0));
        Assert.Equal("Isbn", reader.GetName(1));
        Assert.Equal("Title", reader.GetName(2));
    }

    [Fact]
    public void GetOrdinal_WhenTheNameDiffersInCase_ShouldStillResolveIt()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: true, count: 1, columns: "Isbn");

        // Assert
        Assert.Equal(0, reader.GetOrdinal("__BULK_INDEX"));
        Assert.Equal(1, reader.GetOrdinal("isbn"));
    }

    [Fact]
    public void GetOrdinal_WhenTheNameIsUnknown_ShouldThrow()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: true, count: 1, columns: "Isbn");

        // Act & Assert
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("NotAColumn"));
    }

    [Fact]
    public void GetValues_WhenTheBufferFitsEveryField_ShouldFillItAndReportTheCount()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: true, count: 1, columns: ["Isbn", "Title"]);
        reader.Read();

        // Act
        var values = new object[3];
        var written = reader.GetValues(values);

        // Assert
        Assert.Equal(3, written);
        Assert.Equal([0, "Isbn0001", "Title 1"], values);
    }

    [Fact]
    public void GetValues_WhenTheBufferIsTooShort_ShouldStopAtItsLength()
    {
        // Arrange
        using var reader = BookReader(withOrdinal: true, count: 1, columns: ["Isbn", "Title"]);
        reader.Read();

        // Act
        var values = new object[2];

        // Assert
        Assert.Equal(2, reader.GetValues(values));
    }

    [Fact]
    public void TypedGetters_WhenTheColumnTypeMatches_ShouldCastTheUnderlyingValue()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: false, count: 1, columns: ["Isbn", "Price", "Edition"]);
        reader.Read();

        // Assert
        Assert.Equal("Isbn0001", reader.GetString(0));
        Assert.Equal(1m, reader.GetDecimal(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public void Indexers_WhenReadingAValue_ShouldAcceptAPositionOrAName()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: true, count: 1, columns: "Isbn");
        reader.Read();

        // Assert
        Assert.Equal("Isbn0001", reader[1]);
        Assert.Equal("Isbn0001", reader["Isbn"]);
    }

    [Fact]
    public void NextResult_WhenCalled_ShouldReportThatThereIsOnlyOneResultSet()
    {
        // Arrange & Act
        using var reader = BookReader(withOrdinal: false, count: 1, columns: "Isbn");

        // Assert
        Assert.False(reader.NextResult());
    }

    private BulkDataReader<Book> BookReader(bool withOrdinal, int count, params string[] columns)
        => new(
            items: [.. Enumerable.Range(1, count)
                .Select(index => new Book
                {
                    Isbn = $"Isbn{index:D4}",
                    Title = $"Title {index}",
                    Price = index,
                    Edition = index
                })],
            columns: Columns(_bookMetadata, columns),
            withOrdinal: withOrdinal
        );

    private static ColumnMetadata[] Columns(EntityMetadata metadata, params string[] propertyNames)
        => [.. propertyNames.Select(name => metadata.ByPropertyName[name])];
}
