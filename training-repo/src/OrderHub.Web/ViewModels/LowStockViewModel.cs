using System.ComponentModel.DataAnnotations;

namespace OrderHub.Web.ViewModels;

public class LowStockViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "庫存門檻必須大於 0")]
    [Display(Name = "庫存門檻")]
    public int Threshold { get; set; } = 10;

    public IReadOnlyList<LowStockRowViewModel> Products { get; set; } = Array.Empty<LowStockRowViewModel>();
}

public class LowStockRowViewModel
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int UnitsSoldLast30Days { get; set; }
}
