using System.Linq.Expressions;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Expressions;
using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Expressions;

public class PropertySelectorParserTests
{
    private readonly EntityMetadata _metadata = MetadataCache.For<Book>();

    [Fact]
    public void Parse_WhenGivenASingleReferenceTypeProperty_ShouldResolveOneColumn()
    {
        // Arrange & Act
        var columns = PropertySelectorParser.Parse<Book>(_metadata, book => book.Isbn);

        // Assert
        Assert.Equal(["Isbn"], columns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Parse_WhenTheValueTypeIsBoxed_ShouldSeeThroughTheConversion()
    {
        // Arrange & Act
        var columns = PropertySelectorParser.Parse<Book>(_metadata, book => book.Id);

        // Assert
        Assert.Equal(["Id"], columns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Parse_WhenGivenAnAnonymousType_ShouldResolveEveryMemberInOrder()
    {
        // Arrange & Act
        var columns = PropertySelectorParser.Parse<Book>(
            _metadata,
            book => new { book.Isbn, book.Title, book.Price }
        );

        // Assert
        Assert.Equal(["Isbn", "Title", "Price"], columns.Select(column => column.PropertyName));
    }

    [Fact]
    public void Parse_WhenResolvingAColumn_ShouldReturnTheInstanceFromTheMetadata()
    {
        // Arrange & Act
        var columns = PropertySelectorParser.Parse<Book>(_metadata, book => book.Isbn);

        // Assert
        Assert.Same(_metadata.ByPropertyName["Isbn"], columns[0]);
    }

    [Fact]
    public void Parse_WhenThePropertyIsNotMapped_ShouldThrow()
    {
        // Act & Assert
        var exception = Assert.Throws<BulkConfigurationException>(() =>
            PropertySelectorParser.Parse<Book>(_metadata, _ => new Unmapped().Nope));

        Assert.Contains("Nope", exception.Message);
        Assert.Contains(nameof(Book), exception.Message);
    }

    [Fact]
    public void Parse_WhenTheExpressionIsNotAPropertyAccess_ShouldThrow()
    {
        // Act & Assert
        var exception = Assert.Throws<BulkConfigurationException>(() =>
            PropertySelectorParser.Parse<Book>(_metadata, book => book.Isbn.Length + 1));

        Assert.Contains("property access expression", exception.Message);
    }

    [Fact]
    public void Parse_WhenTheExpressionIsAFieldAccess_ShouldThrow()
        => Assert.Throws<BulkConfigurationException>(() =>
            PropertySelectorParser.Parse<Book>(_metadata, _ => Unmapped.Field));

    [Fact]
    public void Parse_WhenTheSelectorIsNull_ShouldThrowArgumentNull()
    {
        // Arrange
        Expression<Func<Book, object?>> selector = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => PropertySelectorParser.Parse(_metadata, selector));
    }

    [Fact]
    public void ParsePropertyNames_WhenCalled_ShouldNotNeedMetadata()
    {
        // Arrange & Act
        var names = PropertySelectorParser.ParsePropertyNames<Book>(book => new { book.Id, book.Isbn });

        // Assert
        Assert.Equal(["Id", "Isbn"], names);
    }

    [Fact]
    public void ParsePropertyNames_WhenGivenASingleProperty_ShouldReturnJustThatName()
        => Assert.Equal(["Price"], PropertySelectorParser.ParsePropertyNames<Book>(book => book.Price));

    private class Unmapped
    {
        public static readonly string Field = string.Empty;

        public string Nope { get; set; } = string.Empty;
    }
}
