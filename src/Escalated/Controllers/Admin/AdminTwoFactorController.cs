using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/two-factor")]
public class AdminTwoFactorController : ControllerBase
{
    private readonly TwoFactorService _twoFactorService;

    public AdminTwoFactorController(TwoFactorService twoFactorService)
    {
        _twoFactorService = twoFactorService;
    }

    [HttpPost("setup")]
    public IActionResult Setup([FromBody] TwoFactorSetupRequest request)
    {
        var secret = _twoFactorService.GenerateSecret();
        var qrUri = _twoFactorService.GenerateQrUri(secret, request.Email);
        var recoveryCodes = _twoFactorService.GenerateRecoveryCodes();

        return Ok(new
        {
            secret,
            qrUri,
            recoveryCodes
        });
    }

    [HttpPost("verify")]
    public IActionResult Verify([FromBody] TwoFactorVerifyRequest request)
    {
        var valid = _twoFactorService.Verify(request.Secret, request.Code);
        return Ok(new { valid });
    }
}

public record TwoFactorSetupRequest(string Email);
public record TwoFactorVerifyRequest(string Secret, string Code);
