using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using DevopsMvcApp.Models;

namespace DevopsMvcApp.Data;

/// <summary>Application database context backed by SQLite. Manages Identity users and the Product catalog.</summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    /// <summary>Products table for the CRUD catalog feature.</summary>
    public DbSet<Product> Products { get; set; }
}
