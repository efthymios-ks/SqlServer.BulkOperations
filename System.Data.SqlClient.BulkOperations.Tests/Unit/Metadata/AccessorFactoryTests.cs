using System.Data.SqlClient.BulkOperations.Metadata;
using System.Data.SqlClient.BulkOperations.Tests.Shared;
using Xunit;

namespace System.Data.SqlClient.BulkOperations.Tests.Unit.Metadata;

public class AccessorFactoryTests
{
    [Fact]
    public void BuildGetter_WhenThePropertyIsAReferenceType_ShouldReadItsValue()
    {
        // Arrange & Act
        var getter = AccessorFactory.BuildGetter(Property(nameof(Subject.Name)));

        // Assert
        Assert.Equal("hello", getter(new Subject { Name = "hello" }));
    }

    [Fact]
    public void BuildGetter_WhenThePropertyIsAValueType_ShouldReturnItBoxed()
    {
        // Arrange & Act
        var getter = AccessorFactory.BuildGetter(Property(nameof(Subject.Count)));

        // Assert
        Assert.Equal(7, getter(new Subject { Count = 7 }));
    }

    [Fact]
    public void BuildGetter_WhenTheValueIsNull_ShouldReturnNull()
    {
        // Arrange & Act
        var getter = AccessorFactory.BuildGetter(Property(nameof(Subject.Optional)));

        // Assert
        Assert.Null(getter(new Subject()));
    }

    [Fact]
    public void BuildSetter_WhenInvoked_ShouldWriteThroughToTheProperty()
    {
        // Arrange
        var setter = AccessorFactory.BuildSetter(Property(nameof(Subject.Name)))!;
        var subject = new Subject();

        // Act
        setter(subject, "written");

        // Assert
        Assert.Equal("written", subject.Name);
    }

    [Fact]
    public void BuildSetter_WhenThePropertyIsAValueType_ShouldUnboxTheValue()
    {
        // Arrange
        var setter = AccessorFactory.BuildSetter(Property(nameof(Subject.Count)))!;
        var subject = new Subject();

        // Act
        setter(subject, 42);

        // Assert
        Assert.Equal(42, subject.Count);
    }

    [Fact]
    public void BuildSetter_WhenThePropertyIsNullable_ShouldAcceptNull()
    {
        // Arrange
        var setter = AccessorFactory.BuildSetter(Property(nameof(Subject.Optional)))!;
        var subject = new Subject { Optional = 1 };

        // Act
        setter(subject, null);

        // Assert
        Assert.Null(subject.Optional);
    }

    [Fact]
    public void BuildSetter_WhenTheSetterIsPrivate_ShouldStillWrite()
    {
        // Arrange
        var setter = AccessorFactory.BuildSetter(Property(nameof(Subject.PrivatelySet)))!;
        var subject = new Subject();

        // Act
        setter(subject, "written");

        // Assert
        Assert.Equal("written", subject.PrivatelySet);
    }

    [Fact]
    public void BuildSetter_WhenThePropertyIsReadOnly_ShouldReturnNull()
        => Assert.Null(AccessorFactory.BuildSetter(Property(nameof(Subject.Derived))));

    [Fact]
    public void BuildGetter_WhenReusedAcrossInstances_ShouldReadEachOne()
    {
        // Arrange & Act
        var getter = AccessorFactory.BuildGetter(Property(nameof(Subject.Name)));

        // Assert
        Assert.Equal("a", getter(new Subject { Name = "a" }));
        Assert.Equal("b", getter(new Subject { Name = "b" }));
    }

    [Fact]
    public void Accessors_WhenCopyingAMappedEntity_ShouldRoundTripEveryColumn()
    {
        // Arrange
        var metadata = MetadataCache.For<Book>();
        var source = new Book { Id = 3, Isbn = "R", Title = "TEntity", Price = 5m, Edition = 2 };
        var target = new Book();

        // Act
        foreach (var column in metadata.Columns.Where(column => column.Setter is not null))
        {
            column.Setter!(target, column.Getter(source));
        }

        // Assert
        Assert.Equal(3, target.Id);
        Assert.Equal("R", target.Isbn);
        Assert.Equal(5m, target.Price);
    }

    private static System.Reflection.PropertyInfo Property(string name)
        => typeof(Subject).GetProperty(name)!;

    private class Subject
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public int? Optional { get; set; }

        public string PrivatelySet { get; private set; } = string.Empty;

        public string Derived
            => Name + Count;
    }
}
