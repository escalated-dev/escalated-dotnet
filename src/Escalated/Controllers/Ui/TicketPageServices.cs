using Escalated.Configuration;
using Escalated.Data;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Escalated.Ui;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>The services every ticket page controller uses, in one constructor argument.</summary>
public sealed class TicketPageServices
{
    public TicketPageServices(EscalatedDbContext db, TicketService tickets, AssignmentService assignment,
        MacroService macros, TicketSubjectService subjects, UiPresenter present, IAttachmentStorage storage,
        UiPresence presence, IOptions<EscalatedOptions> options)
    {
        Db = db;
        Tickets = tickets;
        Assignment = assignment;
        Macros = macros;
        Subjects = subjects;
        Present = present;
        Storage = storage;
        Presence = presence;
        Options = options;
    }

    public EscalatedDbContext Db { get; }
    public TicketService Tickets { get; }
    public AssignmentService Assignment { get; }
    public MacroService Macros { get; }
    public TicketSubjectService Subjects { get; }
    public UiPresenter Present { get; }
    public IAttachmentStorage Storage { get; }
    public UiPresence Presence { get; }
    public IOptions<EscalatedOptions> Options { get; }
}
