namespace Escalated.Authorization;

public static partial class RoutePermissions
{
    private const string A = "/P/admin";
    private const string G = "/P/agent";
    private const string Staff = "/P/{admin,agent}";
    private const string T = "/P/{admin,agent}/tickets/*";
    private const string View = "ticket.view";
    private const string Edit = "ticket.edit";

    private static List<Entry> Build()
    {
        var map = new List<Entry>
        {
            // ---- Personal ---------------------------------------------------
            E("*", A + "/settings/two-factor"),
            E("*", A + "/settings/two-factor/**"),

            // ---- Tickets (admin and agent pages and JSON) ---------------------
            E("GET", G, View),
            E("GET", G + "/tickets/dashboard", View),
            E("POST", Staff + "/tickets/bulk", View), // plus each action's, see Required()
            E("GET", Staff + "/tickets", View),
            E("GET", T, View),
            E("PUT|PATCH", T, Edit),
            E("POST", T + "/{status,priority,tags,department,snooze,unsnooze,subjects,link}", Edit),
            E("POST", T + "/actions/*", Edit),
            E("POST", T + "/replies/*/pin", Edit),
            E("DELETE", A + "/tickets/*/subjects/*", Edit),
            E("POST", T + "/assign", "ticket.assign"),
            E("POST", T + "/merge", "ticket.merge"),
            E("POST", T + "/split", "ticket.create"),
            E("POST", T + "/{follow,presence}", View),
            E("POST", T + "/macro", "macro.view", Edit),
            E("POST", T + "/reply", "reply.create"), // internal: reply.create_internal
            E("POST", T + "/note", "reply.create_internal"),
            E("GET", A + "/side-conversations/**", View),
            E("POST", A + "/side-conversations", "reply.create"),
            E("POST", A + "/side-conversations/**", "reply.create"),
            E("*", A + "/saved-views", View),
            E("*", A + "/saved-views/**", View),
            E("GET", G + "/mentions", View),
            E("POST", G + "/mentions/*/read", View),

            // ---- Live chat ----------------------------------------------------
            E("GET", G + "/chat/**", View),
            E("POST", Staff + "/chat/**", "reply.create"),

            // ---- Reports ----------------------------------------------------------
            E("GET", A + "/reports", "report.view"),
            E("GET", A + "/reports/**", "report.view"),

            // ---- Knowledge base (pages and JSON) ------------------------------------
            E("GET", A + "/kb-articles/create", "kb.create"),
            E("GET", A + "/kb-articles/*/edit", "kb.edit"),
            E("GET", A + "/{kb-articles,kb-categories}", "kb.view"),
            E("GET", A + "/knowledge-base/**", "kb.view"),
            E("POST", A + "/{kb-articles,knowledge-base/articles}", "kb.create"),
            E("POST", A + "/knowledge-base/articles/*/publish", "kb.publish"),
            E("PUT", A + "/{kb-articles,knowledge-base/articles}/*", "kb.edit"),
            E("POST", A + "/{kb-categories,knowledge-base/categories}", "kb.edit"),
            E("PUT", A + "/kb-categories/*", "kb.edit"),
            E("DELETE", A + "/{kb-articles,kb-categories,knowledge-base/articles}/*", "kb.delete"),

            // ---- Departments ----------------------------------------------------------
            E("GET", A + "/departments/create", "department.create"),
            E("GET", A + "/departments/*/edit", "department.edit"),
            E("GET", A + "/departments", "department.view"),
            E("POST", A + "/departments", "department.create"),
            E("PUT", A + "/departments/*", "department.edit"),
            E("DELETE", A + "/departments/*", "department.delete"),

            // ---- Macros: anyone who may create one may save it; edits manage --------------
            E("GET", A + "/macros", "macro.view"),
            E("POST", A + "/macros", "macro.create"),
            E("*", A + "/macros/**", "macro.manage"),

            // ---- Custom objects: schemas manage, records data ---------------------------------
            E("GET", A + "/custom-objects/*/records", "custom_object.view"),
            E("*", A + "/custom-objects/*/records", "custom_object.data"),
            E("*", A + "/custom-objects/*/records/**", "custom_object.data"),

            // ---- Users, audit ----------------------------------------------------------------
            E("GET", A + "/users", "user.view"),
            E("*", A + "/users/*/role", "user.manage"),
            E("*", A + "/users/*/roles", "role.manage"),
            E("GET", A + "/{audit-log,audit-logs}", "audit.view"),

            // ---- Plugins: the screens; plugin pages and endpoints are the plugin's ------------
            E("GET", A + "/plugins", "plugin.view"),
            E("GET", A + "/plugins/*/**", Array.Empty<string>()),
            E("*", A + "/plugins/**", "plugin.manage"),
            E("*", "/P/api/plugins/**", Array.Empty<string>()),

            // ---- Workflows: the logs are reads ------------------------------------------------
            E("GET", A + "/workflows/{logs,options}", "automation.view"),
            E("GET", A + "/workflows/*/logs", "automation.view"),
        };

        map.AddRange(Resource(A + "/custom-objects", "custom_object"));
        map.AddRange(Resource(A + "/roles", "role"));
        map.AddRange(Resource(A + "/sla-policies", "sla"));
        map.AddRange(Resource(A + "/business-hours", "sla"));
        map.AddRange(Resource(A + "/escalation-rules", "escalation"));
        map.AddRange(Resource(A + "/automations", "automation"));
        map.AddRange(Resource(A + "/workflows", "automation"));
        map.AddRange(Resource(A + "/tags", "tag"));
        map.AddRange(Resource(A + "/custom-fields", "custom_field"));
        map.AddRange(Resource(A + "/statuses", "settings"));
        map.AddRange(Resource(A + "/skills", "user"));
        map.AddRange(Resource(A + "/capacity", "user"));
        map.AddRange(Resource(A + "/webhooks", "webhook"));
        map.AddRange(Resource(A + "/api-tokens", "api_token"));
        map.AddRange(Resource(A + "/canned-responses", "macro"));

        map.AddRange(new[]
        {
            // ---- Settings and imports -----------------------------------------------------
            E("*", A + "/import", "settings.manage"),
            E("*", A + "/import/**", "settings.manage"),
            E("GET", A + "/settings", "settings.view"),
            E("GET", A + "/settings/**", "settings.view"),
            E("*", A + "/settings", "settings.manage"),
            E("*", A + "/settings/**", "settings.manage"),

            // ---- Newsletters (ADR 2026-05-19-newsletter-system); sending also needs newsletters.send
            E("*", "/admin/newsletters", "newsletters.manage"),
            E("*", "/admin/newsletters/**", "newsletters.manage"),
        });

        return map;
    }
}
