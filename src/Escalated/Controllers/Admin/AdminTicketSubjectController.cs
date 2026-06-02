using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Admin;

/// <summary>
/// Attach/detach host-app subject models on a ticket. Types are resolved strictly
/// against <c>Escalated:TicketSubjects:Types</c> so request input cannot reference
/// arbitrary host entities.
/// </summary>
[ApiController]
[Route("support/admin/tickets")]
public class AdminTicketSubjectController : ControllerBase
{
    private readonly TicketService _ticketService;
    private readonly TicketSubjectService _subjectService;
    private readonly ITicketSubjectResolver _resolver;

    public AdminTicketSubjectController(
        TicketService ticketService,
        TicketSubjectService subjectService,
        ITicketSubjectResolver resolver)
    {
        _ticketService = ticketService;
        _subjectService = subjectService;
        _resolver = resolver;
    }

    [HttpPost("{id:int}/subjects")]
    public async Task<IActionResult> Attach(int id, [FromBody] AttachSubjectRequest request, CancellationToken ct)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Type))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["type"] = ["The type field is required."],
            }));

        if (request.Id is null || string.IsNullOrWhiteSpace(request.Id.ToString()))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["id"] = ["The id field is required."],
            }));

        var subjectId = request.Id.ToString()!.Trim();

        try
        {
            var subject = await _resolver.ResolveAsync(request.Type, subjectId, ct);
            if (subject == null)
            {
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["id"] = ["No matching subject was found."],
                }));
            }

            var link = await _subjectService.AttachAsync(
                ticket,
                request.Type,
                subjectId,
                request.Role,
                viaApi: true,
                ct: ct);

            return Ok(link);
        }
        catch (InvalidOperationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["type"] = [ex.Message],
            }));
        }
    }

    [HttpDelete("{ticketId:int}/subjects/{subjectLinkId:int}")]
    public async Task<IActionResult> Detach(int ticketId, int subjectLinkId, CancellationToken ct)
    {
        var ticket = await _ticketService.FindByIdAsync(ticketId);
        if (ticket == null)
            return NotFound();

        var removed = await _subjectService.DetachByLinkIdAsync(ticket, subjectLinkId, ct);
        if (removed == 0)
            return NotFound();

        return Ok(new { message = "Subject detached." });
    }
}

public record AttachSubjectRequest(string Type, object Id, string? Role = null);
