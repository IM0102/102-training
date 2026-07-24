using OrderHub.Core.Domain;

namespace OrderHub.Core.Services;

public interface IProductService
{
    Task<IReadOnlyList<Product>> GetAllAsync();
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task<IReadOnlyList<ProductLowStockItem>> GetLowStockAsync(int threshold);
}
