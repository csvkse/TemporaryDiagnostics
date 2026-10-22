namespace TemporaryDiagnostics.SampleHost.Services;

public interface IOrderService
{
    string GetOrderStatus(int orderId);
    Task<string> ProcessOrderAsync(int orderId, string customerName);
    Task<decimal> CalculateTotalAsync(int orderId, decimal discount);
}

public class OrderService : IOrderService
{
    public string GetOrderStatus(int orderId)
    {
        return $"订单 #{orderId} 状态: 已支付待发货 (处理于 {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss})";
    }

    public async Task<string> ProcessOrderAsync(int orderId, string customerName)
    {
        await Task.Delay(20); // 模拟异步工作
        return $"客户 [{customerName}] 的订单 #{orderId} 异步处理成功！";
    }

    public async Task<decimal> CalculateTotalAsync(int orderId, decimal discount)
    {
        await Task.Delay(10);
        var basePrice = 1200m;
        return (basePrice - discount) * 1.13m;
    }
}

public interface IInventoryCacheService
{
    void InvalidateCache(string sku);
    int GetStockCount(string sku);
    ValueTask<bool> CheckAvailabilityAsync(string sku, int quantity);
}

public class InventoryCacheService : IInventoryCacheService
{
    private static readonly Dictionary<string, int> Stock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SKU-IPHONE16"] = 58,
        ["SKU-MACBOOK-M4"] = 14,
        ["SKU-NVIDIA-5090"] = 3
    };

    public void InvalidateCache(string sku)
    {
        Stock[sku] = 100;
    }

    public int GetStockCount(string sku)
    {
        return Stock.TryGetValue(sku, out var count) ? count : 0;
    }

    public ValueTask<bool> CheckAvailabilityAsync(string sku, int quantity)
    {
        var count = GetStockCount(sku);
        return ValueTask.FromResult(count >= quantity);
    }
}
