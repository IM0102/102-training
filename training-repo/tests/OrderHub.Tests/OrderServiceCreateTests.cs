using OrderHub.Core.Domain;
using OrderHub.Core.Services;

namespace OrderHub.Tests;

public class OrderServiceCreateTests
{
    [Fact]
    public async Task CreateOrder_HappyPath_CreatesPendingOrder()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 2) });

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Pending, result.Value!.Status);
        Assert.Single(result.Value.Items);
        Assert.Equal(1, db.Orders.Count());
    }

    [Fact]
    public async Task CreateOrder_SnapshotsCurrentUnitPrice()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, unitPrice: 380m);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 1) });

        Assert.True(result.Success);
        Assert.Equal(380m, result.Value!.Items.Single().UnitPriceSnapshot);
    }

    [Fact]
    public async Task CreateOrder_GoldCustomer_SnapshotsFullPriceNotPreDiscounted()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db, tier: CustomerTier.Gold);
        var product = TestSetup.AddProduct(db, unitPrice: 1000m);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 1) });

        Assert.True(result.Success);
        var order = result.Value!;
        order.Customer = customer;

        Assert.Equal(1000m, order.Items.Single().UnitPriceSnapshot);
        Assert.Equal(900m, service.CalculateTotal(order));
    }

    [Fact]
    public async Task CreateOrder_DecrementsProductStock()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, stock: 10);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 3) });

        Assert.True(result.Success);
        Assert.Equal(7, db.Products.Single(p => p.Id == product.Id).StockQuantity);
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_Fails()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var product = TestSetup.AddProduct(db);

        var result = await service.CreateOrderAsync(999, new[] { new NewOrderLine(product.Id, 1) });

        Assert.False(result.Success);
        Assert.Contains("客戶", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateOrder_EmptyLines_Fails()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);

        var result = await service.CreateOrderAsync(customer.Id, Array.Empty<NewOrderLine>());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateOrder_NonPositiveQuantity_Fails()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 0) });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateOrder_DuplicateProduct_Fails()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db);

        var result = await service.CreateOrderAsync(customer.Id, new[]
        {
            new NewOrderLine(product.Id, 1),
            new NewOrderLine(product.Id, 2)
        });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateOrder_InactiveProduct_Fails()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, isActive: false);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 1) });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_FailsWithMessage()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, stock: 2);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 5) });

        Assert.False(result.Success);
        Assert.Contains("庫存不足", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateOrder_Failed_DoesNotPersistOrder()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, stock: 2);

        await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 5) });

        Assert.Equal(0, db.Orders.Count());
    }
}
