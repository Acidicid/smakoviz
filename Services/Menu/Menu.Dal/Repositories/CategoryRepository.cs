using Menu.Dal.Context;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

public sealed class CategoryRepository(MenuDbContext context)
    : GenericRepository<Category>(context), ICategoryRepository
{
    public Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<bool> ExistsBySlugOrNameAsync(string slug, string name, CancellationToken ct = default)
        => Set.AnyAsync(c => c.Slug == slug || c.Name == name, ct);
}
