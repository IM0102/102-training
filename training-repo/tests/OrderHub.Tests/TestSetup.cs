using Microsoft.EntityFrameworkCore;
using OrderHub.Core.Domain;
using OrderHub.Core.Services;
using OrderHub.Infrastructure.Data;
using OrderHub.Infrastructure.Repositories;

namespace OrderHub.Tests;

/// <summary>
/// 測試共用工具：使用 EF Core InMemory，讓測試不依賴本機 SQL Server。
/// </summary>
public static class TestSetup
{
    public static OrderHubDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OrderHubDbContext(options);
    }

    public static OrderService CreateOrderService(OrderHubDbContext db) =>
        new(new OrderRepository(db), new ProductRepository(db), new CustomerRepository(db));

    public static ProductService CreateProductService(OrderHubDbContext db) =>
        new(new ProductRepository(db), new OrderRepository(db));

    public static Customer AddCustomer(OrderHubDbContext db, CustomerTier tier = CustomerTier.Standard, string name = "測試客戶")
    {
        var customer = new Customer
        {
            Name = name,
            Email = "test@example.com.tw",
            Tier = tier,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer;
    }

    public static Product AddProduct(OrderHubDbContext db, decimal unitPrice = 100m, int stock = 50, bool isActive = true, string? sku = null)
    {
        var product = new Product
        {
            Sku = sku ?? $"SKU-{Guid.NewGuid():N}"[..12],
            Name = "測試商品",
            UnitPrice = unitPrice,
            StockQuantity = stock,
            IsActive = isActive
        };
        db.Products.Add(product);
        db.SaveChanges();
        return product;
    }
}
