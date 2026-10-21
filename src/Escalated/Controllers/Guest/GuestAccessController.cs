using Escalated.Controllers.Widget;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Escalated.Services;

namespace Escalated.Controllers.Guest;

/// <summary>
/// The guest (browser) prefix of verified guest access: request an email code
/// and look tickets up with it. The widget serves the same endpoints under
/// <c>support/widget</c>. See <c>docs/guest-access.md</c>.
/// </summary>
[ApiController]
[Route("support/guest")]
[AllowAnonymous]
public class GuestAccessController : ControllerBase
{
    /// <summary>Body: <c>{email, purpose: ticket|chat|lookup}</c>. Answers 202 <c>{verification_id, expires_in, message}</c>.</summary>
    [HttpPost("verification")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Verification([FromBody] GuestVerificationRequest request) =>
        GuestAccessEndpoints.VerificationAsync(this, request);

    /// <summary>Body: <c>{email, reference, verification_id, verification_code}</c>. Answers <c>{data: [...]}</c>.</summary>
    [HttpPost("lookup")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Lookup([FromBody] GuestLookupRequest request) =>
        GuestAccessEndpoints.LookupAsync(this, request);
}
