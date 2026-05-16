param(
    [string]$ProjectPath = "../src/DevopsMvcApp"
)

Write-Host "Seeding sample products..." -ForegroundColor Yellow

$script = @"
using DevopsMvcApp.Data;
using DevopsMvcApp.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
db.Database.EnsureCreated();

if (!db.Products.Any())
{
    db.Products.AddRange(
        new Product { Name = "Laptop", Description = "High-performance laptop", Price = 1299.99m, Stock = 10, CreatedAt = DateTime.UtcNow },
        new Product { Name = "Mouse", Description = "Wireless mouse", Price = 29.99m, Stock = 50, CreatedAt = DateTime.UtcNow },
        new Product { Name = "Keyboard", Description = "Mechanical keyboard", Price = 89.99m, Stock = 30, CreatedAt = DateTime.UtcNow },
        new Product { Name = "Monitor", Description = "27-inch 4K monitor", Price = 499.99m, Stock = 15, CreatedAt = DateTime.UtcNow }
    );
    db.SaveChanges();
    WriteLine("Seeded 4 products.");
}
else
{
    WriteLine("Products already exist, skipping seed.");
}
"@

dotnet run --project $ProjectPath
