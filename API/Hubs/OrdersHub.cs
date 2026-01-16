// API/Hubs/OrdersHub.cs
using Microsoft.AspNetCore.SignalR;

public class OrdersHub : Hub
{
    private readonly ILogger<OrdersHub> _logger;

    public OrdersHub(ILogger<OrdersHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinOrdersGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Orders");
        _logger.LogInformation("Client {ConnectionId} joined Orders group", Context.ConnectionId);
    }

    public async Task LeaveOrdersGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Orders");
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

// Notification service per broadcast
public interface IOrderNotificationService
{
    Task NotifyOrderCreated(OrderDto order);
    Task NotifyOrderUpdated(OrderDto order);
    Task NotifyOrderDeleted(int orderId);
}

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrdersHub> _hubContext;

    public OrderNotificationService(IHubContext<OrdersHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyOrderCreated(OrderDto order)
    {
        await _hubContext.Clients
            .Group("Orders")
            .SendAsync("OrderCreated", order);
    }

    public async Task NotifyOrderUpdated(OrderDto order)
    {
        await _hubContext.Clients
            .Group("Orders")
            .SendAsync("OrderUpdated", order);
    }

    public async Task NotifyOrderDeleted(int orderId)
    {
        await _hubContext.Clients
            .Group("Orders")
            .SendAsync("OrderDeleted", orderId);
    }
}
/* Real-Time Updates con SignalR */
