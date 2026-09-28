using Bizkit_backend.DTOs.BusinessTypes;

namespace Bizkit_backend.Services.BusinessTypes;

public interface IBusinessTypeService
{
    Task<IReadOnlyCollection<BusinessTypeResponseDto>> GetAllAsync(
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<BusinessTypeResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<BusinessTypeServiceResult> CreateAsync(
        CreateBusinessTypeRequestDto request,
        CancellationToken cancellationToken = default);

    Task<BusinessTypeServiceResult> UpdateAsync(
        int id,
        UpdateBusinessTypeRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<BusinessTypeProductResponseDto>>
        GetProductsAsync(
            int businessTypeId,
            CancellationToken cancellationToken = default);

    Task<BusinessTypeServiceResult> AssignProductAsync(
        int businessTypeId,
        AssignBusinessTypeProductRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveProductAsync(
        int businessTypeId,
        int productId,
        CancellationToken cancellationToken = default);
}