using System.Security.Claims;
using Bizkit_backend.DTOs.Orders;
using Bizkit_backend.DTOs.Resale;
using Bizkit_backend.Services.Resale;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bizkit_backend.Controllers.Resale;

[ApiController]
[Route("api/resale")]
public sealed class ResaleController(
    IResaleService resaleService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(
        typeof(IReadOnlyCollection<ResaleListingResponseDto>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var listings =
            await resaleService.GetAllAsync(
                cancellationToken);

        return Ok(listings);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(
        typeof(ResaleListingResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var listing =
            await resaleService.GetByIdAsync(
                id,
                cancellationToken);

        if (listing is null)
        {
            return NotFound(new
            {
                message =
                    "Resale listing not found."
            });
        }

        return Ok(listing);
    }

    [HttpGet("eligible-products")]
    [Authorize]
    [ProducesResponseType(
        typeof(IReadOnlyCollection<EligibleResaleProductDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEligibleProducts(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var products = await resaleService.GetEligibleProductsAsync(
            userId,
            cancellationToken);

        return Ok(products);
    }

    [HttpGet("my-listings")]
    [Authorize]
    [ProducesResponseType(
        typeof(IReadOnlyCollection<ResaleListingResponseDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyListings(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var listings = await resaleService.GetMyListingsAsync(
            userId,
            cancellationToken);

        return Ok(listings);
    }

    [HttpPost]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(ResaleListingResponseDto),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromForm] CreateResaleListingRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result =
            await resaleService.CreateAsync(
                userId,
                request,
                cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message =
                    "Resale listing could not be created.",
                errors = result.Errors
            });
        }

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                id = result.Response!.Id
            },
            result.Response);
    }

    [HttpPut("{id:int}")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(ResaleListingResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromForm] UpdateResaleListingRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result =
            await resaleService.UpdateAsync(
                userId,
                id,
                request,
                cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        if (result.Errors.Contains(
                "Resale listing not found."))
        {
            return NotFound(new
            {
                message =
                    "Resale listing not found."
            });
        }

        return BadRequest(new
        {
            message =
                "Resale listing could not be updated.",
            errors = result.Errors
        });
    }

    [HttpDelete("{id:int}")]
    [Authorize]
    [ProducesResponseType(
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var removed =
            await resaleService.RemoveAsync(
                userId,
                id,
                cancellationToken);

        if (!removed)
        {
            return NotFound(new
            {
                message =
                    "Active resale listing not found."
            });
        }

        return Ok(new
        {
            message = "Resale listing removed successfully."
        });
    }

    [HttpPost("{id:int}/purchase")]
    [Authorize]
    [ProducesResponseType(
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purchase(
        int id,
        [FromBody] CreateOrderRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result =
            await resaleService.PurchaseAsync(
                userId,
                id,
                request,
                cancellationToken);

        if (!result.Succeeded)
        {
            if (result.Errors.Contains(
                    "Active resale listing not found."))
            {
                return NotFound(new
                {
                    message =
                        "Active resale listing not found."
                });
            }

            return BadRequest(new
            {
                message =
                    "Resale listing could not be purchased.",
                errors = result.Errors
            });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new
            {
                message =
                    "Resale item purchased successfully.",
                orderId = result.OrderId,
                orderNumber = result.OrderNumber
            });
    }

    private string? GetUserId()
    {
        return User.FindFirstValue(
            ClaimTypes.NameIdentifier);
    }
}