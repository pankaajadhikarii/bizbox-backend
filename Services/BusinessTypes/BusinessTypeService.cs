using Bizkit_backend.Data;
using Bizkit_backend.DTOs.BusinessTypes;
using Bizkit_backend.Models.Entities;
using Bizkit_backend.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace Bizkit_backend.Services.BusinessTypes;

public sealed class BusinessTypeService(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService) : IBusinessTypeService
{
    public async Task<IReadOnlyCollection<BusinessTypeResponseDto>>
    GetAllAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
{
    var query = dbContext.BusinessTypes
        .AsNoTracking()
        .Where(businessType => businessType.IsActive);

    if (!string.IsNullOrWhiteSpace(search))
    {
        var searchTerm = search.Trim();

        query = query.Where(businessType =>
            EF.Functions.ILike(
                businessType.Name,
                $"%{searchTerm}%") ||
            (businessType.Description != null &&
             EF.Functions.ILike(
                 businessType.Description,
                 $"%{searchTerm}%")));
    }

    var businessTypes = await query
        .OrderBy(businessType => businessType.Name)
        .ToListAsync(cancellationToken);

    return businessTypes
        .Select(ToResponse)
        .ToList();
}

    public async Task<BusinessTypeResponseDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var businessType = await dbContext.BusinessTypes
            .AsNoTracking()
            .Include(item => item.BusinessTypeProducts)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(
                item => item.Id == id && item.IsActive,
                cancellationToken);

        return businessType is null
            ? null
            : ToResponse(businessType);
    }

    public async Task<BusinessTypeServiceResult> CreateAsync(
        CreateBusinessTypeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        var description = request.Description.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BusinessTypeServiceResult.Failure(
                "Business type name is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return BusinessTypeServiceResult.Failure(
                "Business type description is required.");
        }

        if (request.Image is null || request.Image.Length == 0)
        {
            return BusinessTypeServiceResult.Failure(
                "Business type image file is required.");
        }

        if (await NameExistsAsync(
                name,
                null,
                cancellationToken))
        {
            return BusinessTypeServiceResult.Failure(
                "A business type with this name already exists.");
        }

        var (succeeded, filePath, errorMessage) = await fileStorageService.SaveFileAsync(
            request.Image,
            "business-types",
            cancellationToken);

        if (!succeeded)
        {
            return BusinessTypeServiceResult.Failure(
                errorMessage ?? "Image upload failed.");
        }

        var businessType = new BusinessType
        {
            Name = name,
            Description = description,
            ImageUrl = filePath!,
            IsActive = true
        };

        dbContext.BusinessTypes.Add(businessType);

        await dbContext.SaveChangesAsync(cancellationToken);

        return BusinessTypeServiceResult.Success(
            ToResponse(businessType));
    }

    public async Task<BusinessTypeServiceResult> UpdateAsync(
        int id,
        UpdateBusinessTypeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var businessType = await dbContext.BusinessTypes
            .Include(item => item.BusinessTypeProducts)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (businessType is null)
        {
            return BusinessTypeServiceResult.Failure(
                "Business type not found.");
        }

        var name = request.Name.Trim();
        var description = request.Description.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BusinessTypeServiceResult.Failure(
                "Business type name is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return BusinessTypeServiceResult.Failure(
                "Business type description is required.");
        }

        if (await NameExistsAsync(
                name,
                id,
                cancellationToken))
        {
            return BusinessTypeServiceResult.Failure(
                "A business type with this name already exists.");
        }

        var imageUrl = businessType.ImageUrl;

        if (request.Image is not null && request.Image.Length > 0)
        {
            var (succeeded, newFilePath, errorMessage) = await fileStorageService.SaveFileAsync(
                request.Image,
                "business-types",
                cancellationToken);

            if (!succeeded)
            {
                return BusinessTypeServiceResult.Failure(
                    errorMessage ?? "Image upload failed.");
            }

            fileStorageService.DeleteFile(businessType.ImageUrl);
            imageUrl = newFilePath;
        }

        businessType.Name = name;
        businessType.Description = description;
        businessType.ImageUrl = imageUrl;

        await dbContext.SaveChangesAsync(cancellationToken);

        return BusinessTypeServiceResult.Success(
            ToResponse(businessType));
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var businessType = await dbContext.BusinessTypes
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (businessType is null)
        {
            return false;
        }

        businessType.IsActive = false;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyCollection<BusinessTypeProductResponseDto>>
        GetProductsAsync(
            int businessTypeId,
            CancellationToken cancellationToken = default)
    {
        return await dbContext.BusinessTypeProducts
            .AsNoTracking()
            .Where(item =>
                item.BusinessTypeId == businessTypeId &&
                item.BusinessType.IsActive &&
                item.Product.IsActive)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Product.Name)
            .Select(item => new BusinessTypeProductResponseDto
            {
                ProductId = item.ProductId,
                ProductName = item.Product.Name,
                Price = item.Product.Price,
                ImageUrl = item.Product.ImageUrl,
                IsRequired = item.IsRequired,
                RecommendedQuantity = item.RecommendedQuantity,
                DisplayOrder = item.DisplayOrder
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BusinessTypeServiceResult> AssignProductAsync(
        int businessTypeId,
        AssignBusinessTypeProductRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.RecommendedQuantity <= 0)
        {
            return BusinessTypeServiceResult.Failure(
                "Recommended quantity must be greater than zero.");
        }

        var businessType = await dbContext.BusinessTypes
            .FirstOrDefaultAsync(
                item =>
                    item.Id == businessTypeId &&
                    item.IsActive,
                cancellationToken);

        if (businessType is null)
        {
            return BusinessTypeServiceResult.Failure(
                "Active business type not found.");
        }

        var productExists = await dbContext.Products
            .AnyAsync(
                item =>
                    item.Id == request.ProductId &&
                    item.IsActive,
                cancellationToken);

        if (!productExists)
        {
            return BusinessTypeServiceResult.Failure(
                "Active product not found.");
        }

        var mapping = await dbContext.BusinessTypeProducts
            .FirstOrDefaultAsync(
                item =>
                    item.BusinessTypeId == businessTypeId &&
                    item.ProductId == request.ProductId,
                cancellationToken);

        if (mapping is null)
        {
            mapping = new BusinessTypeProduct
            {
                BusinessTypeId = businessTypeId,
                ProductId = request.ProductId
            };

            dbContext.BusinessTypeProducts.Add(mapping);
        }

        mapping.IsRequired = request.IsRequired;
        mapping.RecommendedQuantity = request.RecommendedQuantity;
        mapping.DisplayOrder = request.DisplayOrder;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await GetByIdForAdminAsync(
            businessTypeId,
            cancellationToken);

        return BusinessTypeServiceResult.Success(response!);
    }

    public async Task<bool> RemoveProductAsync(
        int businessTypeId,
        int productId,
        CancellationToken cancellationToken = default)
    {
        var mapping = await dbContext.BusinessTypeProducts
            .FirstOrDefaultAsync(
                item =>
                    item.BusinessTypeId == businessTypeId &&
                    item.ProductId == productId,
                cancellationToken);

        if (mapping is null)
        {
            return false;
        }

        dbContext.BusinessTypeProducts.Remove(mapping);

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<bool> NameExistsAsync(
        string name,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.ToLower();

        return await dbContext.BusinessTypes
            .AnyAsync(
                item =>
                    item.Name.ToLower() == normalizedName &&
                    (!excludedId.HasValue ||
                     item.Id != excludedId.Value),
                cancellationToken);
    }

    private async Task<BusinessTypeResponseDto?> GetByIdForAdminAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var businessType = await dbContext.BusinessTypes
            .AsNoTracking()
            .Include(item => item.BusinessTypeProducts)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        return businessType is null
            ? null
            : ToResponse(businessType);
    }

    private static BusinessTypeResponseDto ToResponse(
        BusinessType businessType)
    {
        return new BusinessTypeResponseDto
        {
            Id = businessType.Id,
            Name = businessType.Name,
            Description = businessType.Description,
            ImageUrl = businessType.ImageUrl,
            IsActive = businessType.IsActive,
            Products = businessType.BusinessTypeProducts
                .Where(item => item.Product.IsActive)
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Product.Name)
                .Select(item => new BusinessTypeProductResponseDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    Price = item.Product.Price,
                    ImageUrl = item.Product.ImageUrl,
                    IsRequired = item.IsRequired,
                    RecommendedQuantity = item.RecommendedQuantity,
                    DisplayOrder = item.DisplayOrder
                })
                .ToList()
        };
    }
}