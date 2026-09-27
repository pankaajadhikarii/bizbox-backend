using Bizkit_backend.Data;
using Bizkit_backend.DTOs.Orders;
using Bizkit_backend.DTOs.Payments;
using Bizkit_backend.Models.Entities;
using Bizkit_backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bizkit_backend.Services.Orders;

public sealed class OrderService(
    ApplicationDbContext dbContext) : IOrderService
{
    public async Task<IReadOnlyCollection<OrderResponseDto>>
        GetMyOrdersAsync(
            string customerId,
            CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.OrderItems)
            .Include(order => order.Payments)
            .Where(order => order.CustomerId == customerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        return orders
            .Select(ToResponse)
            .ToList();
    }

    public async Task<OrderResponseDto?> GetByIdAsync(
        int orderId,
        string customerId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.OrderItems)
            .Include(order => order.Payments)
            .Where(order => order.Id == orderId);

        if (!isAdmin)
        {
            query = query.Where(
                order => order.CustomerId == customerId);
        }

        var order = await query
            .FirstOrDefaultAsync(cancellationToken);

        return order is null
            ? null
            : ToResponse(order);
    }

    public async Task<OrderServiceResult> CreateAsync(
        string customerId,
        CreateOrderRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var shippingAddress =
            request.ShippingAddress?.Trim();

        if (string.IsNullOrWhiteSpace(shippingAddress))
        {
            return OrderServiceResult.Failure(
                "Shipping address is required.");
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var cart = await dbContext.Carts
            .Include(item => item.CartItems)
                .ThenInclude(item => item.Product)
                    .ThenInclude(product => product.Category)
            .FirstOrDefaultAsync(
                item => item.UserId == customerId,
                cancellationToken);

        if (cart is null ||
            cart.CartItems.Count == 0)
        {
            return OrderServiceResult.Failure(
                "Cart is empty.");
        }

        foreach (var cartItem in cart.CartItems)
        {
            var product = cartItem.Product;

            if (!product.IsActive ||
                !product.Category.IsActive)
            {
                return OrderServiceResult.Failure(
                    $"Product '{product.Name}' is no longer available.");
            }

            if (cartItem.Quantity <= 0)
            {
                return OrderServiceResult.Failure(
                    $"Invalid quantity for '{product.Name}'.");
            }

            if (cartItem.Quantity > product.StockQuantity)
            {
                return OrderServiceResult.Failure(
                    $"Only {product.StockQuantity} item(s) of '{product.Name}' are available.");
            }
        }

        var now = DateTime.UtcNow;

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(),
            CustomerId = customerId,
            ShippingAddress = shippingAddress,
            PaymentMethod = request.PaymentMethod,

            PaymentStatus =
                request.PaymentMethod ==
                PaymentMethod.CashOnDelivery
                    ? OrderPaymentStatus.Unpaid
                    : OrderPaymentStatus.Pending,

            Status = OrderStatus.Pending,

            OrderDate = now,
            UpdatedAt = now
        };

        var customer = await dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Id == customerId,
                cancellationToken);
        if (customer is not null)
        {
            order.Customer = customer;
        }

        foreach (var cartItem in cart.CartItems)
        {
            var product = cartItem.Product;

            var unitPrice = product.Price;

            var subtotal =
                unitPrice * cartItem.Quantity;

            var orderItem = new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = unitPrice,
                Quantity = cartItem.Quantity,
                Subtotal = subtotal
            };

            order.OrderItems.Add(orderItem);

            product.StockQuantity -=
                cartItem.Quantity;

            product.UpdatedAt = now;

            order.TotalAmount += subtotal;
        }

        order.Payments.Add(new Payment
        {
            PaymentMethod = request.PaymentMethod,
            Amount = order.TotalAmount,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.Orders.Add(order);

        dbContext.CartItems.RemoveRange(
            cart.CartItems);

        cart.UpdatedAt = now;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return OrderServiceResult.Success(
            ToResponse(order));
    }

    public async Task<IReadOnlyCollection<OrderResponseDto>>
        GetAllAsync(
            CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.OrderItems)
            .Include(order => order.Payments)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        return orders
            .Select(ToResponse)
            .ToList();
    }

    public async Task<OrderServiceResult> UpdateStatusAsync(
        int orderId,
        UpdateOrderStatusRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .Include(item => item.Customer)
            .Include(item => item.OrderItems)
            .Include(item => item.Payments)
            .FirstOrDefaultAsync(
                item => item.Id == orderId,
                cancellationToken);

        if (order is null)
        {
            return OrderServiceResult.Failure(
                "Order not found.");
        }

        if (order.Status == request.Status)
        {
            return OrderServiceResult.Failure(
                "Order is already in this status.");
        }

        if (!IsValidStatusTransition(
                order.Status,
                request.Status))
        {
            return OrderServiceResult.Failure(
                $"Order cannot move from '{order.Status}' to '{request.Status}'.");
        }

        if (request.Status == OrderStatus.Cancelled)
        {
            foreach (var orderItem in order.OrderItems)
            {
                var product = await dbContext.Products
                    .FirstOrDefaultAsync(
                        item => item.Id == orderItem.ProductId,
                        cancellationToken);

                if (product is not null)
                {
                    product.StockQuantity +=
                        orderItem.Quantity;

                    product.UpdatedAt =
                        DateTime.UtcNow;
                }
            }
        }

        order.Status = request.Status;
        order.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return OrderServiceResult.Success(
            ToResponse(order));
    }

    private static bool IsValidStatusTransition(
        OrderStatus currentStatus,
        OrderStatus newStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending =>
                newStatus == OrderStatus.Processing ||
                newStatus == OrderStatus.Cancelled,

            OrderStatus.Processing =>
                newStatus == OrderStatus.Shipped ||
                newStatus == OrderStatus.Cancelled,

            OrderStatus.Shipped =>
                newStatus == OrderStatus.Delivered,

            OrderStatus.Delivered =>
                false,

            OrderStatus.Cancelled =>
                false,

            _ => false
        };
    }

    private static string GenerateOrderNumber()
    {
        return $"BZ-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
    }

    private static OrderResponseDto ToResponse(
        Order order)
    {
        return new OrderResponseDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            TotalAmount = order.TotalAmount,
            ShippingAddress = order.ShippingAddress,
            CustomerName = !string.IsNullOrWhiteSpace(order.Customer?.FullName)
                ? order.Customer.FullName
                : (order.Customer?.UserName ?? order.Customer?.Email),
            CustomerEmail = order.Customer?.Email,
            PaymentStatus = order.PaymentStatus,
            PaymentMethod = order.PaymentMethod,
            Status = order.Status,
            OrderDate = order.OrderDate,
            UpdatedAt = order.UpdatedAt,

            Items = order.OrderItems
                .OrderBy(item => item.Id)
                .Select(item => new OrderItemResponseDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ResaleListingId =
                        item.ResaleListingId,
                    ProductName = item.ProductName,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity,
                    Subtotal = item.Subtotal
                })
                .ToList(),

            Payments = order.Payments
                .OrderByDescending(
                    payment => payment.CreatedAt)
                .Select(payment =>
                    new PaymentResponseDto
                    {
                        Id = payment.Id,
                        OrderId = payment.OrderId,
                        PaymentMethod =
                            payment.PaymentMethod,
                        Amount = payment.Amount,
                        TransactionReference =
                            payment.TransactionReference,
                        Status = payment.Status,
                        PaidAt = payment.PaidAt,
                        CreatedAt = payment.CreatedAt,
                        UpdatedAt = payment.UpdatedAt
                    })
                .ToList()
        };
    }
}