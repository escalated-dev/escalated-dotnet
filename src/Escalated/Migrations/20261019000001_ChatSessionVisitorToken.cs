using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Escalated.Migrations;

/// <summary>
/// Adds the hashed visitor token that widget chat routes are keyed by.
/// Existing chats get no token, so the widget can no longer reach them.
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20261019000001_ChatSessionVisitorToken")]
public sealed class ChatSessionVisitorToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("VisitorTokenHash", "escalated_chat_sessions", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex("IX_escalated_chat_sessions_VisitorTokenHash", "escalated_chat_sessions", "VisitorTokenHash");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_escalated_chat_sessions_VisitorTokenHash", "escalated_chat_sessions");
        migrationBuilder.DropColumn("VisitorTokenHash", "escalated_chat_sessions");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        EscalatedModelConfiguration.Configure(modelBuilder);
    }
}
