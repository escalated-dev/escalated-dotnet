using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Escalated.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialEscalatedSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escalated_agent_capacity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxConcurrent = table.Column<int>(type: "integer", nullable: false),
                    CurrentCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_agent_capacity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_agent_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AgentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MaxTickets = table.Column<int>(type: "integer", nullable: false),
                    Signature = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_agent_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_api_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenableType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TokenableId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Abilities = table.Column<string>(type: "text", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_api_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_article_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_article_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_article_categories_escalated_article_categories_P~",
                        column: x => x.ParentId,
                        principalTable: "escalated_article_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalated_attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttachableType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AttachableId = table.Column<int>(type: "integer", nullable: false),
                    Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Disk = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OldValues = table.Column<string>(type: "text", nullable: true),
                    NewValues = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_automations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Conditions = table.Column<string>(type: "text", nullable: false),
                    Actions = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_automations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_business_schedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_business_schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_canned_responses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_canned_responses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    MarketingOptOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_custom_fields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Options = table.Column<string>(type: "text", nullable: true),
                    ValidationRules = table.Column<string>(type: "text", nullable: true),
                    Conditions = table.Column<string>(type: "text", nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_custom_fields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_custom_objects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldsSchema = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_custom_objects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_escalation_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Conditions = table.Column<string>(type: "text", nullable: false),
                    Actions = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_escalation_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_import_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Credentials = table.Column<string>(type: "text", nullable: true),
                    FieldMappings = table.Column<string>(type: "text", nullable: true),
                    Progress = table.Column<string>(type: "text", nullable: true),
                    Errors = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_import_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_macros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Actions = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_macros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_newsletter_lists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FilterJson = table.Column<string>(type: "json", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_newsletter_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_newsletter_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Theme = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectTemplate = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    MergeFieldsSchema = table.Column<string>(type: "json", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_newsletter_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_plugins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_plugins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_saved_views",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Filters = table.Column<string>(type: "text", nullable: false),
                    SortBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SortDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_saved_views", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_followers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_followers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_statuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_webhooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Secret = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Events = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_webhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_workflows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    trigger_event = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Conditions = table.Column<string>(type: "text", nullable: false),
                    Actions = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    stop_on_match = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_articles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    AuthorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    HelpfulCount = table.Column<int>(type: "integer", nullable: false),
                    NotHelpfulCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_articles_escalated_article_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "escalated_article_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "escalated_holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Recurring = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_holidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_holidays_escalated_business_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "escalated_business_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_custom_object_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ObjectId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_custom_object_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_custom_object_records_escalated_custom_objects_Ob~",
                        column: x => x.ObjectId,
                        principalTable: "escalated_custom_objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_chat_routing_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    AgentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Conditions = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_chat_routing_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_chat_routing_rules_escalated_departments_Departme~",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "escalated_department_agent",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "integer", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_department_agent", x => new { x.DepartmentId, x.AgentId });
                    table.ForeignKey(
                        name: "FK_escalated_department_agent_escalated_departments_Department~",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_sla_policies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    TicketType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FirstResponseHours = table.Column<string>(type: "text", nullable: true),
                    ResolutionHours = table.Column<string>(type: "text", nullable: true),
                    BusinessHoursOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_sla_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_sla_policies_escalated_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalated_import_source_maps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EscalatedId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_import_source_maps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_import_source_maps_escalated_import_jobs_ImportJo~",
                        column: x => x.ImportJobId,
                        principalTable: "escalated_import_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_newsletter_list_members",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ListId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_newsletter_list_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_newsletter_list_members_escalated_contacts_Contac~",
                        column: x => x.ContactId,
                        principalTable: "escalated_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_newsletter_list_members_escalated_newsletter_list~",
                        column: x => x.ListId,
                        principalTable: "escalated_newsletter_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_newsletters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    FromEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ReplyTo = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    TargetListId = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    Theme = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SentBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SummaryTotal = table.Column<int>(type: "integer", nullable: false),
                    SummarySent = table.Column<int>(type: "integer", nullable: false),
                    SummaryOpened = table.Column<int>(type: "integer", nullable: false),
                    SummaryClicked = table.Column<int>(type: "integer", nullable: false),
                    SummaryBounced = table.Column<int>(type: "integer", nullable: false),
                    SummaryComplained = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_newsletters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_newsletters_escalated_newsletter_lists_TargetList~",
                        column: x => x.TargetListId,
                        principalTable: "escalated_newsletter_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalated_newsletters_escalated_newsletter_templates_Templa~",
                        column: x => x.TemplateId,
                        principalTable: "escalated_newsletter_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "escalated_role_permission",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    PermissionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_role_permission", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_escalated_role_permission_escalated_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "escalated_permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_role_permission_escalated_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "escalated_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_role_user",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_role_user", x => new { x.RoleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_escalated_role_user_escalated_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "escalated_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_agent_skill",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false),
                    Proficiency = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_agent_skill", x => x.Id);
                    table.CheckConstraint("CK_escalated_agent_skill_proficiency", "\"Proficiency\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_escalated_agent_skill_escalated_skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "escalated_skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_skill_routing_departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SkillId = table.Column<int>(type: "integer", nullable: false),
                    DepartmentId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_skill_routing_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_skill_routing_departments_escalated_departments_D~",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_skill_routing_departments_escalated_skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "escalated_skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_skill_routing_tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SkillId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_skill_routing_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_skill_routing_tags_escalated_skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "escalated_skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_skill_routing_tags_escalated_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "escalated_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_webhook_deliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WebhookId = table.Column<int>(type: "integer", nullable: false),
                    Event = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_webhook_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_webhook_deliveries_escalated_webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "escalated_webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_tickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TicketType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AccountType = table.Column<string>(type: "character varying(191)", maxLength: 191, nullable: true, collation: "C"),
                    AccountId = table.Column<string>(type: "character varying(191)", maxLength: 191, nullable: true, collation: "C"),
                    RequesterType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RequesterId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AssignedTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    SlaPolicyId = table.Column<int>(type: "integer", nullable: true),
                    MergedIntoId = table.Column<int>(type: "integer", nullable: true),
                    GuestName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    GuestEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    GuestToken = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    FirstResponseAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstResponseDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaFirstResponseBreached = table.Column<bool>(type: "boolean", nullable: false),
                    SlaResolutionBreached = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnoozedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_tickets_escalated_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "escalated_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_escalated_tickets_escalated_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_escalated_tickets_escalated_sla_policies_SlaPolicyId",
                        column: x => x.SlaPolicyId,
                        principalTable: "escalated_sla_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_escalated_tickets_escalated_tickets_MergedIntoId",
                        column: x => x.MergedIntoId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalated_newsletter_deliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NewsletterId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    EmailAtSend = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TrackingToken = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClicksCount = table.Column<int>(type: "integer", nullable: false),
                    BounceReason = table.Column<string>(type: "text", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<short>(type: "smallint", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_newsletter_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_newsletter_deliveries_escalated_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "escalated_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_newsletter_deliveries_escalated_newsletters_Newsl~",
                        column: x => x.NewsletterId,
                        principalTable: "escalated_newsletters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_chat_sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VisitorEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AgentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DepartmentId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    VisitorTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_chat_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_chat_sessions_escalated_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "escalated_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_escalated_chat_sessions_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_custom_field_values",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomFieldId = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TicketId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_custom_field_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_custom_field_values_escalated_custom_fields_Custo~",
                        column: x => x.CustomFieldId,
                        principalTable: "escalated_custom_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_custom_field_values_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "escalated_inbound_emails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FromEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FromName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ToEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    BodyText = table.Column<string>(type: "text", nullable: true),
                    BodyHtml = table.Column<string>(type: "text", nullable: true),
                    InReplyTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Adapter = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TicketId = table.Column<int>(type: "integer", nullable: true),
                    ReplyId = table.Column<int>(type: "integer", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_inbound_emails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_inbound_emails_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "escalated_replies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsInternalNote = table.Column<bool>(type: "boolean", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AuthorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    InReplyTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_replies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_replies_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_satisfaction_ratings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    RaterType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RaterId = table.Column<int>(type: "integer", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_satisfaction_ratings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_satisfaction_ratings_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_side_conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_side_conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_side_conversations_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CauserType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CauserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_activities_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentTicketId = table.Column<int>(type: "integer", nullable: false),
                    ChildTicketId = table.Column<int>(type: "integer", nullable: false),
                    LinkType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_links_escalated_tickets_ChildTicketId",
                        column: x => x.ChildTicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_links_escalated_tickets_ParentTicketId",
                        column: x => x.ParentTicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_subjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_subjects_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_tag",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_tag", x => new { x.TicketId, x.TagId });
                    table.ForeignKey(
                        name: "FK_escalated_ticket_tag_escalated_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "escalated_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_tag_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_workflow_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<int>(type: "integer", nullable: false),
                    ticket_id = table.Column<int>(type: "integer", nullable: false),
                    trigger_event = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    conditions_matched = table.Column<bool>(type: "boolean", nullable: false),
                    actions_executed = table.Column<string>(type: "text", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_workflow_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_workflow_logs_escalated_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_workflow_logs_escalated_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "escalated_workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_mentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReplyId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_mentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_mentions_escalated_replies_ReplyId",
                        column: x => x.ReplyId,
                        principalTable: "escalated_replies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escalated_side_conversation_replies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SideConversationId = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_side_conversation_replies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_side_conversation_replies_escalated_side_conversa~",
                        column: x => x.SideConversationId,
                        principalTable: "escalated_side_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_agent_capacity_UserId_Channel",
                table: "escalated_agent_capacity",
                columns: new[] { "UserId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_agent_profiles_UserId",
                table: "escalated_agent_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_agent_skill_SkillId",
                table: "escalated_agent_skill",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_agent_skill_UserId_SkillId",
                table: "escalated_agent_skill",
                columns: new[] { "UserId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_api_tokens_TokenHash",
                table: "escalated_api_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_article_categories_ParentId",
                table: "escalated_article_categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_article_categories_Slug",
                table: "escalated_article_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_articles_CategoryId",
                table: "escalated_articles",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_articles_Slug",
                table: "escalated_articles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_attachments_AttachableType_AttachableId",
                table: "escalated_attachments",
                columns: new[] { "AttachableType", "AttachableId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_audit_logs_CreatedAt",
                table: "escalated_audit_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_audit_logs_EntityType_EntityId",
                table: "escalated_audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_audit_logs_UserId",
                table: "escalated_audit_logs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_routing_rules_DepartmentId",
                table: "escalated_chat_routing_rules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_routing_rules_Priority",
                table: "escalated_chat_routing_rules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_sessions_AgentId",
                table: "escalated_chat_sessions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_sessions_DepartmentId",
                table: "escalated_chat_sessions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_sessions_Status",
                table: "escalated_chat_sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_sessions_TicketId",
                table: "escalated_chat_sessions",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_chat_sessions_VisitorTokenHash",
                table: "escalated_chat_sessions",
                column: "VisitorTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_contacts_Email",
                table: "escalated_contacts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_contacts_MarketingOptOutAt",
                table: "escalated_contacts",
                column: "MarketingOptOutAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_contacts_UserId",
                table: "escalated_contacts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_field_values_CustomFieldId",
                table: "escalated_custom_field_values",
                column: "CustomFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_field_values_EntityType_EntityId",
                table: "escalated_custom_field_values",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_field_values_TicketId",
                table: "escalated_custom_field_values",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_fields_Slug",
                table: "escalated_custom_fields",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_object_records_ObjectId",
                table: "escalated_custom_object_records",
                column: "ObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_objects_Slug",
                table: "escalated_custom_objects",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_departments_Slug",
                table: "escalated_departments",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_holidays_ScheduleId",
                table: "escalated_holidays",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_import_source_maps_unique_source",
                table: "escalated_import_source_maps",
                columns: new[] { "ImportJobId", "EntityType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_inbound_emails_MessageId",
                table: "escalated_inbound_emails",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_inbound_emails_TicketId",
                table: "escalated_inbound_emails",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_mentions_ReplyId_UserId",
                table: "escalated_mentions",
                columns: new[] { "ReplyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_mentions_UserId",
                table: "escalated_mentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_deliveries_ContactId",
                table: "escalated_newsletter_deliveries",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_deliveries_NewsletterId_Status",
                table: "escalated_newsletter_deliveries",
                columns: new[] { "NewsletterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_deliveries_Status_ClaimedAt",
                table: "escalated_newsletter_deliveries",
                columns: new[] { "Status", "ClaimedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_deliveries_TrackingToken",
                table: "escalated_newsletter_deliveries",
                column: "TrackingToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_list_members_ContactId",
                table: "escalated_newsletter_list_members",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_list_members_ListId_ContactId",
                table: "escalated_newsletter_list_members",
                columns: new[] { "ListId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_lists_CreatedBy",
                table: "escalated_newsletter_lists",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletter_lists_Kind",
                table: "escalated_newsletter_lists",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_CreatedBy",
                table: "escalated_newsletters",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_ScheduledAt",
                table: "escalated_newsletters",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_Status",
                table: "escalated_newsletters",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_Status_ScheduledAt",
                table: "escalated_newsletters",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_TargetListId",
                table: "escalated_newsletters",
                column: "TargetListId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_newsletters_TemplateId",
                table: "escalated_newsletters",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_permissions_Slug",
                table: "escalated_permissions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_plugins_Slug",
                table: "escalated_plugins",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_replies_MessageId",
                table: "escalated_replies",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_replies_TicketId",
                table: "escalated_replies",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_role_permission_PermissionId",
                table: "escalated_role_permission",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_roles_Slug",
                table: "escalated_roles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_satisfaction_ratings_TicketId",
                table: "escalated_satisfaction_ratings",
                column: "TicketId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_settings_Key",
                table: "escalated_settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_side_conversation_replies_SideConversationId",
                table: "escalated_side_conversation_replies",
                column: "SideConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_side_conversations_TicketId",
                table: "escalated_side_conversations",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_skill_routing_departments_DepartmentId",
                table: "escalated_skill_routing_departments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_skill_routing_departments_SkillId_DepartmentId",
                table: "escalated_skill_routing_departments",
                columns: new[] { "SkillId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_skill_routing_tags_SkillId_TagId",
                table: "escalated_skill_routing_tags",
                columns: new[] { "SkillId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_skill_routing_tags_TagId",
                table: "escalated_skill_routing_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_skills_Slug",
                table: "escalated_skills",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_sla_policies_DepartmentId_TicketType",
                table: "escalated_sla_policies",
                columns: new[] { "DepartmentId", "TicketType" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tags_Slug",
                table: "escalated_tags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_activities_CreatedAt",
                table: "escalated_ticket_activities",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_activities_TicketId",
                table: "escalated_ticket_activities",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_followers_TicketId_UserId",
                table: "escalated_ticket_followers",
                columns: new[] { "TicketId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_followers_UserId",
                table: "escalated_ticket_followers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_links_ChildTicketId",
                table: "escalated_ticket_links",
                column: "ChildTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_links_ParentTicketId_ChildTicketId",
                table: "escalated_ticket_links",
                columns: new[] { "ParentTicketId", "ChildTicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_statuses_Slug",
                table: "escalated_ticket_statuses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_subjects_SubjectType_SubjectId",
                table: "escalated_ticket_subjects",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_subjects_TicketId_SubjectType_SubjectId",
                table: "escalated_ticket_subjects",
                columns: new[] { "TicketId", "SubjectType", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_tag_TagId",
                table: "escalated_ticket_tag",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_AccountType_AccountId",
                table: "escalated_tickets",
                columns: new[] { "AccountType", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_AssignedTo",
                table: "escalated_tickets",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_ContactId",
                table: "escalated_tickets",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_CreatedAt",
                table: "escalated_tickets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_DepartmentId",
                table: "escalated_tickets",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_GuestEmail",
                table: "escalated_tickets",
                column: "GuestEmail");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_GuestToken",
                table: "escalated_tickets",
                column: "GuestToken");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_MergedIntoId",
                table: "escalated_tickets",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_Priority",
                table: "escalated_tickets",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_Reference",
                table: "escalated_tickets",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_RequesterType_RequesterId",
                table: "escalated_tickets",
                columns: new[] { "RequesterType", "RequesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_SlaPolicyId",
                table: "escalated_tickets",
                column: "SlaPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_Status",
                table: "escalated_tickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_webhook_deliveries_WebhookId",
                table: "escalated_webhook_deliveries",
                column: "WebhookId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_workflow_logs_ticket_id",
                table: "escalated_workflow_logs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_workflow_logs_workflow_id",
                table: "escalated_workflow_logs",
                column: "workflow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_agent_capacity");

            migrationBuilder.DropTable(
                name: "escalated_agent_profiles");

            migrationBuilder.DropTable(
                name: "escalated_agent_skill");

            migrationBuilder.DropTable(
                name: "escalated_api_tokens");

            migrationBuilder.DropTable(
                name: "escalated_articles");

            migrationBuilder.DropTable(
                name: "escalated_attachments");

            migrationBuilder.DropTable(
                name: "escalated_audit_logs");

            migrationBuilder.DropTable(
                name: "escalated_automations");

            migrationBuilder.DropTable(
                name: "escalated_canned_responses");

            migrationBuilder.DropTable(
                name: "escalated_chat_routing_rules");

            migrationBuilder.DropTable(
                name: "escalated_chat_sessions");

            migrationBuilder.DropTable(
                name: "escalated_custom_field_values");

            migrationBuilder.DropTable(
                name: "escalated_custom_object_records");

            migrationBuilder.DropTable(
                name: "escalated_department_agent");

            migrationBuilder.DropTable(
                name: "escalated_escalation_rules");

            migrationBuilder.DropTable(
                name: "escalated_holidays");

            migrationBuilder.DropTable(
                name: "escalated_import_source_maps");

            migrationBuilder.DropTable(
                name: "escalated_inbound_emails");

            migrationBuilder.DropTable(
                name: "escalated_macros");

            migrationBuilder.DropTable(
                name: "escalated_mentions");

            migrationBuilder.DropTable(
                name: "escalated_newsletter_deliveries");

            migrationBuilder.DropTable(
                name: "escalated_newsletter_list_members");

            migrationBuilder.DropTable(
                name: "escalated_plugins");

            migrationBuilder.DropTable(
                name: "escalated_role_permission");

            migrationBuilder.DropTable(
                name: "escalated_role_user");

            migrationBuilder.DropTable(
                name: "escalated_satisfaction_ratings");

            migrationBuilder.DropTable(
                name: "escalated_saved_views");

            migrationBuilder.DropTable(
                name: "escalated_settings");

            migrationBuilder.DropTable(
                name: "escalated_side_conversation_replies");

            migrationBuilder.DropTable(
                name: "escalated_skill_routing_departments");

            migrationBuilder.DropTable(
                name: "escalated_skill_routing_tags");

            migrationBuilder.DropTable(
                name: "escalated_ticket_activities");

            migrationBuilder.DropTable(
                name: "escalated_ticket_followers");

            migrationBuilder.DropTable(
                name: "escalated_ticket_links");

            migrationBuilder.DropTable(
                name: "escalated_ticket_statuses");

            migrationBuilder.DropTable(
                name: "escalated_ticket_subjects");

            migrationBuilder.DropTable(
                name: "escalated_ticket_tag");

            migrationBuilder.DropTable(
                name: "escalated_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "escalated_workflow_logs");

            migrationBuilder.DropTable(
                name: "escalated_article_categories");

            migrationBuilder.DropTable(
                name: "escalated_custom_fields");

            migrationBuilder.DropTable(
                name: "escalated_custom_objects");

            migrationBuilder.DropTable(
                name: "escalated_business_schedules");

            migrationBuilder.DropTable(
                name: "escalated_import_jobs");

            migrationBuilder.DropTable(
                name: "escalated_replies");

            migrationBuilder.DropTable(
                name: "escalated_newsletters");

            migrationBuilder.DropTable(
                name: "escalated_permissions");

            migrationBuilder.DropTable(
                name: "escalated_roles");

            migrationBuilder.DropTable(
                name: "escalated_side_conversations");

            migrationBuilder.DropTable(
                name: "escalated_skills");

            migrationBuilder.DropTable(
                name: "escalated_tags");

            migrationBuilder.DropTable(
                name: "escalated_webhooks");

            migrationBuilder.DropTable(
                name: "escalated_workflows");

            migrationBuilder.DropTable(
                name: "escalated_newsletter_lists");

            migrationBuilder.DropTable(
                name: "escalated_newsletter_templates");

            migrationBuilder.DropTable(
                name: "escalated_tickets");

            migrationBuilder.DropTable(
                name: "escalated_contacts");

            migrationBuilder.DropTable(
                name: "escalated_sla_policies");

            migrationBuilder.DropTable(
                name: "escalated_departments");
        }
    }
}
