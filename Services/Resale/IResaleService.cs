using Bizkit_backend.DTOs.Orders;
using Bizkit_backend.DTOs.Resale;

namespace Bizkit_backend.Services.Resale;

public interface IResaleService
{
    Task<IReadOnlyCollection<ResaleListingResponseDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ResaleListingResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EligibleResaleProductDto>> GetEligibleProductsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ResaleListingResponseDto>> GetMyListingsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<ResaleServiceResult> CreateAsync(
        string sellerId,
        CreateResaleListingRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ResaleServiceResult> UpdateAsync(
        string sellerId,
        int id,
        UpdateResaleListingRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        string sellerId,
        int id,
        CancellationToken cancellationToken = default);

    Task<ResaleServiceResult> PurchaseAsync(
        string buyerId,
        int id,
        CreateOrderRequestDto request,
        CancellationToken cancellationToken = default);
}