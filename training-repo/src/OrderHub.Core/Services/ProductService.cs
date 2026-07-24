using OrderHub.Core.Domain;
using OrderHub.Core.Interfaces;

namespace OrderHub.Core.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;

    public ProductService(IProductRepository productRepository, IOrderRepository orderRepository)
    {
        _productRepository = productRepository;
        _orderRepository = orderRepository;
    }

    public Task<IReadOnlyList<Product>> GetAllAsync() => _productRepository.GetAllAsync();

    public Task<IReadOnlyList<Product>> GetActiveAsync() => _productRepository.GetActiveAsync();

    public async Task<IReadOnlyList<ProductLowStockItem>> GetLowStockAsync(int threshold)
    {
        var products = await _productRepository.GetLowStockAsync(threshold);
        if (products.Count == 0)
            return Array.Empty<ProductLowStockItem>();

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var unitsSold = await _orderRepository.GetUnitsSoldSinceAsync(
            products.Select(p => p.Id).ToList(), cutoff);

        return products
            .Select(p => new ProductLowStockItem(p, unitsSold.GetValueOrDefault(p.Id)))
            .ToList();
    }
}
