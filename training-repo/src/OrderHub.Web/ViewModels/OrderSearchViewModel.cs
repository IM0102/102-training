namespace OrderHub.Web.ViewModels;

public class OrderSearchViewModel
{
    public string Query { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public List<OrderRowViewModel> Orders { get; set; } = new();

    public bool HasSearched => !string.IsNullOrWhiteSpace(Query);
}
