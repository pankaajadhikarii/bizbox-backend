using Bizkit_backend.DTOs.BusinessTypes;
using Bizkit_backend.Services.BusinessTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bizkit_backend.Controllers.BusinessTypes;

[ApiController]
[Route("api/business-types")]
public sealed class BusinessTypesController(
    IBusinessTypeService businessTypeService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(
        typeof(IReadOnlyCollection<BusinessTypeResponseDto>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
    [FromQuery] string? search,
    CancellationToken cancellationToken)
{
    var businessTypes = await businessTypeService.GetAllAsync(
        search,
        cancellationToken);

    return Ok(businessTypes);
}

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(
        typeof(BusinessTypeResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var businessType = await businessTypeService.GetByIdAsync(
            id,
            cancellationToken);

        if (businessType is null)
        {
            return NotFound(new
            {
                message = "Business type not found."
            });
        }

        return Ok(businessType);
    }

    [HttpGet("{id:int}/products")]
    [AllowAnonymous]
    [ProducesResponseType(
        typeof(IReadOnlyCollection<BusinessTypeProductResponseDto>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(
        int id,
        CancellationToken cancellationToken)
    {
        var products = await businessTypeService.GetProductsAsync(
            id,
            cancellationToken);

        return Ok(products);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(BusinessTypeResponseDto),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] CreateBusinessTypeRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await businessTypeService.CreateAsync(
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Business type creation failed.",
                errors = result.Errors
            });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Response!.Id },
            result.Response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(BusinessTypeResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromForm] UpdateBusinessTypeRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await businessTypeService.UpdateAsync(
            id,
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            if (result.Errors.Contains("Business type not found."))
            {
                return NotFound(new
                {
                    message = "Business type not found."
                });
            }

            return BadRequest(new
            {
                message = "Business type update failed.",
                errors = result.Errors
            });
        }

        return Ok(result.Response);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var deleted = await businessTypeService.DeleteAsync(
            id,
            cancellationToken);

        if (!deleted)
        {
            return NotFound(new
            {
                message = "Business type not found."
            });
        }

        return Ok(new
        {
            message = "Business type deleted successfully."
        });
    }

    [HttpPost("{id:int}/products")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(
        typeof(BusinessTypeResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignProduct(
        int id,
        [FromBody] AssignBusinessTypeProductRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await businessTypeService.AssignProductAsync(
            id,
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Product assignment failed.",
                errors = result.Errors
            });
        }

        return Ok(result.Response);
    }

    [HttpDelete("{id:int}/products/{productId:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveProduct(
        int id,
        int productId,
        CancellationToken cancellationToken)
    {
        var removed = await businessTypeService.RemoveProductAsync(
            id,
            productId,
            cancellationToken);

        if (!removed)
        {
            return NotFound(new
            {
                message = "Product assignment not found."
            });
        }

        return Ok(new
        {
            message = "Product removed from business type successfully."
        });
    }
}