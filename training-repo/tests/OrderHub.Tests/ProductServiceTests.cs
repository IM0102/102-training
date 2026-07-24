using OrderHub.Core.Domain;

namespace OrderHub.Tests;

public class ProductServiceTests
{
    [Fact]
    public async Task GetAll_ReturnsAllProductsIncludingInactive()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001");
        TestSetup.AddProduct(db, sku: "SKU-A002", isActive: false);

        var products = await service.GetAllAsync();

        Assert.Equal(2, products.Count);
    }

    [Fact]
    public async Task GetActive_ExcludesInactiveProducts()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001");
        TestSetup.AddProduct(db, sku: "SKU-A002", isActive: false);

        var products = await service.GetActiveAsync();

        Assert.All(products, p => Assert.True(p.IsActive));
        Assert.Single(products);
    }

    [Fact]
    public async Task GetLowStock_FiltersByThresholdAndOrdersByStockAscending()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001", stock: 20);
        var low2 = TestSetup.AddProduct(db, sku: "SKU-A002", stock: 8);
        var low1 = TestSetup.AddProduct(db, sku: "SKU-A003", stock: 3);

        var result = await service.GetLowStockAsync(10);

        Assert.Equal(2, result.Count);
        Assert.Equal(low1.Id, result[0].Product.Id);
        Assert.Equal(low2.Id, result[1].Product.Id);
    }

    [Fact]
    public async Task GetLowStock_ExcludesInactiveProducts()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001", stock: 3, isActive: false);
        var active = TestSetup.AddProduct(db, sku: "SKU-A002", stock: 5);

        var result = await service.GetLowStockAsync(10);

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Product.Id);
    }

    [Fact]
    public async Task GetLowStock_UnitsSoldLast30Days_ExcludesCancelledAndOldOrders()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, stock: 5);

        db.Orders.AddRange(
            new Order
            {
                CustomerId = customer.Id,
                Status = OrderStatus.Confirmed,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                Items = { new OrderItem { ProductId = product.Id, Quantity = 4, UnitPriceSnapshot = product.UnitPrice } }
            },
            new Order
            {
                CustomerId = customer.Id,
                Status = OrderStatus.Cancelled,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                Items = { new OrderItem { ProductId = product.Id, Quantity = 100, UnitPriceSnapshot = product.UnitPrice } }
            },
            new Order
            {
                CustomerId = customer.Id,
                Status = OrderStatus.Confirmed,
                CreatedAt = DateTime.UtcNow.AddDays(-40),
                Items = { new OrderItem { ProductId = product.Id, Quantity = 100, UnitPriceSnapshot = product.UnitPrice } }
            });
        db.SaveChanges();

        var result = await service.GetLowStockAsync(10);

        Assert.Single(result);
        Assert.Equal(4, result[0].UnitsSoldLast30Days);
    }
}
