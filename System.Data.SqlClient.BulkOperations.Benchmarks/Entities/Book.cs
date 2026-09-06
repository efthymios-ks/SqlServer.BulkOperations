using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace System.Data.SqlClient.BulkOperations.Benchmarks.Entities;

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

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
