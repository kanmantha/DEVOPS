using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DevopsMvcApp.Data;
using DevopsMvcApp.Models;

namespace DevopsMvcApp.Controllers;

/// <summary>
/// Products CRUD controller — manages the local Product catalog backed by SQLite.
/// Requires authentication; all actions operate on the ApplicationDbContext directly.
/// Supports search, sort (name/price/stock/date), and pagination (10 per page).
/// </summary>
[Authorize]
public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProductsController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Lists products with optional search, sort, and pagination (10/page).</summary>
    public async Task<IActionResult> Index(string? search, string? sortBy, string? sortDir, int p = 1)
    {
        const int pageSize = 10;
        var query = _context.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || (p.Description != null && p.Description.Contains(search)));

        sortBy ??= "name";
        sortDir ??= "asc";
        bool asc = sortDir == "asc";
        query = (sortBy.ToLower()) switch
        {
            "price" => asc ? query.OrderBy(p => p.Price) : query.OrderByDescending(p => p.Price),
            "stock" => asc ? query.OrderBy(p => p.Stock) : query.OrderByDescending(p => p.Stock),
            "created" => asc ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt),
            _ => asc ? query.OrderBy(p => p.Name) : query.OrderByDescending(p => p.Name),
        };

        var total = await query.CountAsync();
        var items = await query.Skip((p - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Search = search;
        ViewBag.SortBy = sortBy;
        ViewBag.SortDir = sortDir;
        ViewBag.Page = p;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.Total = total;

        return View(items);
    }

    /// <summary>Shows details for a single product.</summary>
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products.FirstOrDefaultAsync(m => m.Id == id);
        if (product == null) return NotFound();
        return View(product);
    }

    /// <summary>Shows the create-product form.</summary>
    public IActionResult Create()
    {
        return View();
    }

    /// <summary>Creates a new product and saves to the database.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
        if (ModelState.IsValid)
        {
            product.CreatedAt = DateTime.UtcNow;
            _context.Add(product);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(product);
    }

    /// <summary>Shows the edit-product form pre-filled with current values.</summary>
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound();
        return View(product);
    }

    /// <summary>Saves edited product data to the database.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product product)
    {
        if (id != product.Id) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(product);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Products.Any(e => e.Id == product.Id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(product);
    }

    /// <summary>Shows the delete confirmation page.</summary>
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products.FirstOrDefaultAsync(m => m.Id == id);
        if (product == null) return NotFound();
        return View(product);
    }

    /// <summary>Deletes a product from the database after confirmation.</summary>
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product != null) _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
