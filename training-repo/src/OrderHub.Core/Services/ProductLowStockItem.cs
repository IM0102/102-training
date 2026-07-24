using OrderHub.Core.Domain;

namespace OrderHub.Core.Services;

public record ProductLowStockItem(Product Product, int UnitsSoldLast30Days);
