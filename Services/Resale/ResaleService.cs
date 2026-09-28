using Bizkit_backend.Data;
using Bizkit_backend.DTOs.Orders;
using Bizkit_backend.DTOs.Resale;
using Bizkit_backend.Models.Entities;
using Bizkit_backend.Models.Enums;
using Bizkit_backend.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace Bizkit_backend.Services.Resale;

public sealed class ResaleService(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService) : IResaleService
{
    public async Task<IReadOnlyCollection<ResaleListingResponseDto>>
        GetAllAsync(
            CancellationToken cancellationToken = default)
    {
        var listings = await dbContext.ResaleListings
            .AsNoTracking()
            .Include(listing => listing.Product)
            .Include(listing => listing.Seller)
            .Where(listing =>
                listing.Status == ResaleListingStatus.Active)
            .OrderByDescending(listing => listing.CreatedAt)
            .ToListAsync(cancellationToken);

        return listings
            .Select(ToResponse)
            .ToList();
    }

    public async Task<ResaleListingResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var listing = await dbContext.ResaleListings
            .AsNoTracking()
            .Include(listing => listing.Product)
            .Include(listing => listing.Seller)
            .FirstOrDefaultAsync(
                listing =>
                    listing.Id == id &&
                    listing.Status ==
                        ResaleListingStatus.Active,
                cancellationToken);

        return listing is null
            ? null
            : ToResponse(listing);
    }

    public async Task<IReadOnlyCollection<EligibleResaleProductDto>> GetEligibleProductsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Get distinct product IDs from delivered orders belonging to this user
        var purchasedProductIds = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item =>
                item.Order.CustomerId == userId &&
                item.Order.Status == OrderStatus.Delivered &&
                item.ResaleListingId == null) // only original purchases, not resale buys
            .Select(item => item.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (purchasedProductIds.Count == 0)
        {
            return [];
        }

        // Get currently active resale listing IDs for this user
        var alreadyListedProductIds = await dbContext.ResaleListings
            .AsNoTracking()
            .Where(listing =>
                listing.SellerId == userId &&
                listing.Status == ResaleListingStatus.Active)
            .Select(listing => listing.ProductId)
            .ToHashSetAsync(cancellationToken);

        // Fetch product details for eligible products
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p =>
                purchasedProductIds.Contains(p.Id) &&
                p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new EligibleResaleProductDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                ProductImageUrl = p.ImageUrl,
                OriginalPrice = p.Price,
                AlreadyListed = alreadyListedProductIds.Contains(p.Id)
            })
            .ToListAsync(cancellationToken);

        return products;
    }

    public async Task<IReadOnlyCollection<ResaleListingResponseDto>> GetMyListingsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var listings = await dbContext.ResaleListings
            .AsNoTracking()
            .Include(listing => listing.Product)
            .Include(listing => listing.Seller)
            .Include(listing => listing.OrderItems)
                .ThenInclude(oi => oi.Order)
                .ThenInclude(o => o.Customer)
            .Where(listing => listing.SellerId == userId)
            .OrderByDescending(listing => listing.CreatedAt)
            .ToListAsync(cancellationToken);

        return listings
            .Select(ToResponse)
            .ToList();
    }

    public async Task<ResaleServiceResult> CreateAsync(
        string sellerId,
        CreateResaleListingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Price <= 0)
        {
            return ResaleServiceResult.Failure(
                "Resale price must be greater than zero.");
        }

        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                product => product.Id == request.ProductId,
                cancellationToken);

        if (product is null)
        {
            return ResaleServiceResult.Failure(
                "Product not found.");
        }

        var ownsProduct = await dbContext.OrderItems
            .AnyAsync(
                item =>
                    item.ProductId == request.ProductId &&
                    item.Order.CustomerId == sellerId &&
                    item.Order.Status ==
                        OrderStatus.Delivered,
                cancellationToken);

        if (!ownsProduct)
        {
            return ResaleServiceResult.Failure(
                "You can only resell products that you purchased through BizKit and received.");
        }

        var alreadyListed = await dbContext.ResaleListings
            .AnyAsync(
                listing =>
                    listing.SellerId == sellerId &&
                    listing.ProductId == request.ProductId &&
                    listing.Status ==
                        ResaleListingStatus.Active,
                cancellationToken);

        if (alreadyListed)
        {
            return ResaleServiceResult.Failure(
                "You already have an active listing for this product.");
        }

        var seller = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                user => user.Id == sellerId,
                cancellationToken);

        if (seller is null)
        {
            return ResaleServiceResult.Failure(
                "User not found.");
        }

        string? imageUrl = null;

        if (request.Image is not null && request.Image.Length > 0)
        {
            var (succeeded, filePath, errorMessage) = await fileStorageService.SaveFileAsync(
                request.Image,
                "resale",
                cancellationToken);

            if (!succeeded)
            {
                return ResaleServiceResult.Failure(errorMessage ?? "Resale image upload failed.");
            }

            imageUrl = filePath;
        }

        var now = DateTime.UtcNow;

        var listing = new ResaleListing
        {
            ProductId = product.Id,
            SellerId = sellerId,
            Price = request.Price,
            Condition = request.Condition,
            Description = request.Description?.Trim(),
            ImageUrl = imageUrl,
            Status = ResaleListingStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ResaleListings.Add(listing);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        listing.Product = product;
        listing.Seller = seller;

        return ResaleServiceResult.Success(
            ToResponse(listing));
    }

    public async Task<ResaleServiceResult> UpdateAsync(
        string sellerId,
        int id,
        UpdateResaleListingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Price <= 0)
        {
            return ResaleServiceResult.Failure(
                "Resale price must be greater than zero.");
        }

        var listing = await dbContext.ResaleListings
            .Include(item => item.Product)
            .Include(item => item.Seller)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (listing is null)
        {
            return ResaleServiceResult.Failure(
                "Resale listing not found.");
        }

        if (listing.SellerId != sellerId)
        {
            return ResaleServiceResult.Failure(
                "You can only modify your own listing.");
        }

        if (listing.Status !=
            ResaleListingStatus.Active)
        {
            return ResaleServiceResult.Failure(
                "Only active listings can be updated.");
        }

        var imageUrl = listing.ImageUrl;

        if (request.Image is not null && request.Image.Length > 0)
        {
            var (succeeded, newFilePath, errorMessage) = await fileStorageService.SaveFileAsync(
                request.Image,
                "resale",
                cancellationToken);

            if (!succeeded)
            {
                return ResaleServiceResult.Failure(errorMessage ?? "Resale image upload failed.");
            }

            fileStorageService.DeleteFile(listing.ImageUrl);
            imageUrl = newFilePath;
        }

        listing.Price = request.Price;
        listing.Condition = request.Condition;
        listing.Description =
            request.Description?.Trim();
        listing.ImageUrl = imageUrl;
        listing.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return ResaleServiceResult.Success(
            ToResponse(listing));
    }

    public async Task<bool> RemoveAsync(
        string sellerId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var listing = await dbContext.ResaleListings
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (listing is null ||
            listing.SellerId != sellerId ||
            listing.Status !=
                ResaleListingStatus.Active)
        {
            return false;
        }

        listing.Status = ResaleListingStatus.Removed;
        listing.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    public async Task<ResaleServiceResult> PurchaseAsync(
        string buyerId,
        int id,
        CreateOrderRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                request.ShippingAddress))
        {
            return ResaleServiceResult.Failure(
                "Shipping address is required.");
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        var listing = await dbContext.ResaleListings
            .Include(item => item.Product)
            .FirstOrDefaultAsync(
                item =>
                    item.Id == id &&
                    item.Status ==
                        ResaleListingStatus.Active,
                cancellationToken);

        if (listing is null)
        {
            return ResaleServiceResult.Failure(
                "Active resale listing not found.");
        }

        if (listing.SellerId == buyerId)
        {
            return ResaleServiceResult.Failure(
                "You cannot purchase your own listing.");
        }

        var buyerExists = await dbContext.Users
            .AnyAsync(
                user => user.Id == buyerId,
                cancellationToken);

        if (!buyerExists)
        {
            return ResaleServiceResult.Failure(
                "Buyer account not found.");
        }

        var now = DateTime.UtcNow;

        var order = new Order
        {
            OrderNumber =
                GenerateOrderNumber(),
            CustomerId = buyerId,
            TotalAmount = listing.Price,
            ShippingAddress =
                request.ShippingAddress.Trim(),
            PaymentMethod =
                request.PaymentMethod,
            PaymentStatus =
                request.PaymentMethod ==
                    PaymentMethod.CashOnDelivery
                    ? OrderPaymentStatus.Unpaid
                    : OrderPaymentStatus.Pending,
            Status = OrderStatus.Pending,
            OrderDate = now,
            UpdatedAt = now
        };

        order.OrderItems.Add(
            new OrderItem
            {
                ProductId = listing.ProductId,
                ResaleListingId = listing.Id,
                ProductName = listing.Product.Name,
                UnitPrice = listing.Price,
                Quantity = 1,
                Subtotal = listing.Price
            });

        order.Payments.Add(
            new Payment
            {
                PaymentMethod =
                    request.PaymentMethod,
                Amount = listing.Price,
                Status = PaymentStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });

        listing.Status =
            ResaleListingStatus.Sold;

        listing.UpdatedAt = now;

        dbContext.Orders.Add(order);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return ResaleServiceResult.PurchaseSuccess(
            order.Id,
            order.OrderNumber);
    }

    private static string GenerateOrderNumber()
    {
        return $"BZ-RESALE-{DateTime.UtcNow:yyyyMMddHHmmss}-" +
               Guid.NewGuid().ToString("N");
    }

    private static ResaleListingResponseDto ToResponse(
        ResaleListing listing)
    {
        var orderItem = listing.OrderItems.LastOrDefault();
        var order = orderItem?.Order;
        var buyer = order?.Customer;

        return new ResaleListingResponseDto
        {
            Id = listing.Id,
            ProductId = listing.ProductId,
            ProductName = listing.Product.Name,
            SellerId = listing.SellerId,
            SellerName = listing.Seller.FullName,
            Price = listing.Price,
            Condition = listing.Condition,
            Description = listing.Description,
            ImageUrl = listing.ImageUrl,
            Status = listing.Status,
            CreatedAt = listing.CreatedAt,
            UpdatedAt = listing.UpdatedAt,
            BuyerName = buyer?.FullName,
            BuyerEmail = buyer?.Email,
            ShippingAddress = order?.ShippingAddress
        };
    }
}