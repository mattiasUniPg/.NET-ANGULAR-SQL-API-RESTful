// API/Controllers/OrdersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace EnterpriseAPI.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[Produces("application/json")]
[EnableRateLimiting("fixed")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// Recupera ordini paginati con filtri
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResponse<OrderDto>>> GetOrders(
        [FromQuery] OrderQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching orders with parameters: {@Parameters}", parameters);

        var result = await _orderService.GetOrdersAsync(parameters, cancellationToken);
        
        return Ok(result);
    }

    /// <summary>
    /// Recupera ordine per ID
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "id" })]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(
        int id,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", id);
            return NotFound(new ProblemDetails
            {
                Title = "Order not found",
                Detail = $"Order with ID {id} does not exist",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(order);
    }

    /// <summary>
    /// Crea nuovo ordine
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new order for customer {CustomerId}", request.CustomerId);

        try
        {
            var order = await _orderService.CreateOrderAsync(request, cancellationToken);
            
            return CreatedAtAction(
                nameof(GetOrder),
                new { id = order.Id },
                order);
        }
        catch (BusinessValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for order creation");
            return UnprocessableEntity(new ValidationProblemDetails(ex.Errors));
        }
    }

    /// <summary>
    /// Aggiorna stato ordine
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOrderStatus(
        int id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orderService.UpdateOrderStatusAsync(id, request.NewStatus, cancellationToken);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict updating order {OrderId}", id);
            return Conflict(new ProblemDetails
            {
                Title = "Concurrency conflict",
                Detail = "The order was modified by another user. Please refresh and try again.",
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>
    /// Elimina ordine (soft delete)
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteOrder(int id, CancellationToken cancellationToken)
    {
        var deleted = await _orderService.DeleteOrderAsync(id, cancellationToken);
        
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Esporta ordini in Excel
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> ExportOrders(
        [FromQuery] OrderQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var fileBytes = await _orderService.ExportOrdersAsync(parameters, cancellationToken);
        
        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"orders_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}

// Application/DTOs/OrderDto.cs
public record OrderDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}

public record OrderDetailDto : OrderDto
{
    public List<OrderItemDto> Items { get; init; } = new();
    public CustomerDto Customer { get; init; } = null!;
    public DateTime? ShippingDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public string? As400OrderId { get; init; }
    public string? SapOrderId { get; init; }
}

// Application/Services/OrderService.cs
public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderService> _logger;
    private readonly IAs400Integration _as400;
    private readonly ISapIntegration _sap;

    public OrderService(
        IRepository<Order> orderRepository,
        IRepository<Customer> customerRepository,
        IMapper mapper,
        ILogger<OrderService> logger,
        IAs400Integration as400,
        ISapIntegration sap)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _mapper = mapper;
        _logger = logger;
        _as400 = as400;
        _sap = sap;
    }

    public async Task<PaginatedResponse<OrderDto>> GetOrdersAsync(
        OrderQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var spec = new OrdersFilterSpec(parameters);
        
        var orders = await _orderRepository.GetPagedAsync(
            parameters.PageNumber,
            parameters.PageSize,
            spec,
            cancellationToken);

        var orderDtos = _mapper.Map<List<OrderDto>>(orders.Items);

        return new PaginatedResponse<OrderDto>(
            orderDtos,
            orders.TotalCount,
            orders.PageIndex,
            orders.PageSize);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Validazione business logic
        var customer = await _customerRepository.GetByIdAsync(
            request.CustomerId, 
            cancellationToken);

        if (customer == null)
            throw new BusinessValidationException("Customer", "Customer not found");

        if (customer.Status != CustomerStatus.Active)
            throw new BusinessValidationException("Customer", "Customer is not active");

        // Creazione ordine
        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(cancellationToken),
            OrderDate = DateTime.UtcNow,
            CustomerId = request.CustomerId,
            Status = OrderStatus.Draft
        };

        // Aggiungi items
        foreach (var item in request.Items)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Discount = item.Discount
            });
        }

        // Calcola totali
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        order.VatAmount = order.TotalAmount * 0.22m;

        // Salva
        await _orderRepository.AddAsync(order, cancellationToken);

        // Integrazione AS/400 asincrona (se configurato)
        if (_as400.IsEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var as400Id = await _as400.CreateOrderAsync(order);
                    order.As400OrderId = as400Id;
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync order {OrderId} to AS/400", order.Id);
                }
            }, cancellationToken);
        }

        return _mapper.Map<OrderDetailDto>(order);
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var count = await _orderRepository.CountAsync(
            new OrdersByYearSpec(year),
            cancellationToken);
        
        return $"ORD-{year}-{(count + 1):D6}";
    }
}
/* API Controllers con Best Practices */
