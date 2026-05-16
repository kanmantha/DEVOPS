using System.ComponentModel.DataAnnotations;

namespace DevopsMvcApp.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    [DataType(DataType.Currency)]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    [Display(Name = "Created At")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
