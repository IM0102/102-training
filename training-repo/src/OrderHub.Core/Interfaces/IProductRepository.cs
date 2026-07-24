using OrderHub.Core.Domain;

namespace OrderHub.Core.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync();
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task<IReadOnlyList<Product>> GetLowStockAsync(int threshold);
    Task<Product?> GetByIdAsync(int id);
    Task SaveChangesAsync();
}
