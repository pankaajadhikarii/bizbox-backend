using System.Security.Claims;
using Bizkit_backend.DTOs.Payments;
using Bizkit_backend.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bizkit_backend.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Authorize]
public sealed class PaymentsController(
    IPaymentService paymentService,
    IEsewaService esewaService) : ControllerBase
{
    [HttpGet("{id:int}")]
    [ProducesResponseType(
        typeof(PaymentResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var payment = await paymentService.GetByIdAsync(
            id,
            userId,
            User.IsInRole("Admin"),
            cancellationToken);

        if (payment is null)
        {
            return NotFound(new
            {
                message = "Payment not found."
            });
        }

        return Ok(payment);
    }

    [HttpPost("esewa/initiate")]
    [ProducesResponseType(
        typeof(EsewaPaymentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InitiateEsewa(
        [FromBody] EsewaInitiateRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await esewaService.InitiateAsync(
            request.OrderId,
            userId,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        if (result.Errors.Contains("Order not found."))
        {
            return NotFound(new
            {
                message = "Order not found."
            });
        }

        return BadRequest(new
        {
            message = "eSewa payment could not be started.",
            errors = result.Errors
        });
    }

    [HttpPost("esewa/verify")]
    [ProducesResponseType(
        typeof(PaymentResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyEsewa(
        [FromBody] EsewaVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await esewaService.VerifyAsync(
            request.Data,
            userId,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        if (result.Errors.Contains("Payment not found."))
        {
            return NotFound(new
            {
                message = "Payment not found."
            });
        }

        return BadRequest(new
        {
            message = "eSewa payment could not be verified.",
            errors = result.Errors
        });
    }

    [HttpPatch("admin/{id:int}/cod-paid")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(
        typeof(PaymentResponseDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkCodAsPaid(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.MarkCodAsPaidAsync(
            id,
            cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        if (result.Errors.Contains(
                "Payment not found."))
        {
            return NotFound(new
            {
                message = "Payment not found."
            });
        }

        return BadRequest(new
        {
            message = "COD payment could not be completed.",
            errors = result.Errors
        });
    }

    private string? GetUserId()
    {
        return User.FindFirstValue(
            ClaimTypes.NameIdentifier);
    }
}
