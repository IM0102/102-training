using Microsoft.AspNetCore.Mvc;
using OrderHub.Core.Services;
using OrderHub.Web.ViewModels;

namespace OrderHub.Web.Controllers;

public class ProductsController : Controller
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    public async Task<IActionResult> Index()
    {
        var products = await _productService.GetAllAsync();

        var vm = new ProductListViewModel
        {
            Products = products.Select(p => new ProductRowViewModel
            {
                Sku = p.Sku,
                Name = p.Name,
                UnitPrice = p.UnitPrice,
                StockQuantity = p.StockQuantity,
                IsActive = p.IsActive
            }).ToList()
        };

        return View(vm);
    }

    public async Task<IActionResult> LowStock(LowStockViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Products = Array.Empty<LowStockRowViewModel>();
            return View(vm);
        }

        var items = await _productService.GetLowStockAsync(vm.Threshold);

        vm.Products = items.Select(i => new LowStockRowViewModel
        {
            Sku = i.Product.Sku,
            Name = i.Product.Name,
            StockQuantity = i.Product.StockQuantity,
            UnitsSoldLast30Days = i.UnitsSoldLast30Days
        }).ToList();

        return View(vm);
    }
}

