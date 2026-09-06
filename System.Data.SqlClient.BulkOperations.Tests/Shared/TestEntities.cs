using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace System.Data.SqlClient.BulkOperations.Tests.Shared;

/// <summary>Identity key, unique natural key, a computed column and a rowversion token.</summary>
[Table("Books", Schema = "dbo")]
public class Book
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Edition { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime CreatedUtc { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}

/// <summary>A slim projection with no [Table] of its own, so callers name the target explicitly.</summary>
public class IsbnOnly
{
    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;
}

/// <summary>Convention-named key, no [Table] schema, no concurrency token.</summary>
[Table("Widgets")]
public class Widget
{
    public int WidgetId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public WidgetStatus Status { get; set; }
}

public enum WidgetStatus
{
    Draft = 0,
    Active = 1,
    Retired = 2
}

/// <summary>Non-dbo schema and a caller-assigned Guid key rather than an identity.</summary>
[Table("Items", Schema = "shop")]
public class ShopItem
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string Sku { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool Active { get; set; }
}

/// <summary>Property names that deliberately differ from their column names.</summary>
[Table("Widgets")]
public class WidgetDto
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>Book columns without a [Table] attribute, so the target has to be named explicitly.</summary>
public class BookRow
{
    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public decimal Price { get; set; }
}
