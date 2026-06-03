using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Escalated.Migrations;

/// <summary>
/// Skills parity (#58): explicit routing pivots + <see cref="AgentSkill"/> surrogate key, timestamps, proficiency 1..5.
/// Agent-skill DDL is intentionally provider-scripted — See <see cref="EscalatedModelConfiguration"/>.
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20260517143000_SkillManagementParity")]
public partial class SkillManagementParity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "escalated_skill_routing_tags",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                SkillId = table.Column<int>(type: "int", nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false),
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
            name: "escalated_skill_routing_departments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                SkillId = table.Column<int>(type: "int", nullable: false),
                DepartmentId = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_skill_routing_departments", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_skill_routing_departments_escalated_departments_DepartmentId",
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

        migrationBuilder.CreateIndex(
            name: "IX_escalated_skill_routing_departments_SkillId_DepartmentId",
            table: "escalated_skill_routing_departments",
            columns: ["SkillId", "DepartmentId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_escalated_skill_routing_tags_SkillId_TagId",
            table: "escalated_skill_routing_tags",
            columns: ["SkillId", "TagId"],
            unique: true);

        MigrateAgentSkills(migrationBuilder);
    }

    private static void MigrateAgentSkills(MigrationBuilder migrationBuilder)
    {
        switch (migrationBuilder.ActiveProvider)
        {
            case "Microsoft.EntityFrameworkCore.Sqlite":
                migrationBuilder.Sql(
                    """

                    CREATE TABLE escalated_agent_skill_par (
                        Id INTEGER NOT NULL CONSTRAINT PK_escalated_agent_skill PRIMARY KEY AUTOINCREMENT,
                        UserId TEXT NOT NULL,
                        SkillId INTEGER NOT NULL,
                        Proficiency INTEGER NOT NULL DEFAULT 3,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        CONSTRAINT FK_escalated_agent_skill_escalated_skills_SkillId FOREIGN KEY (SkillId) REFERENCES escalated_skills (Id) ON DELETE CASCADE,
                        CONSTRAINT CK_escalated_agent_skill_proficiency CHECK (Proficiency BETWEEN 1 AND 5),
                        UNIQUE (UserId, SkillId)
                    );

                    INSERT INTO escalated_agent_skill_par (UserId, SkillId, Proficiency, CreatedAt, UpdatedAt)
                    SELECT
                        UserId,
                        SkillId,
                        CASE lower(trim(ifnull(Proficiency, '')))
                          WHEN 'beginner' THEN 1
                          WHEN 'intermediate' THEN 3
                          WHEN 'expert' THEN 5
                          ELSE 3 END,
                        datetime('now'),
                        datetime('now')
                    FROM escalated_agent_skill;

                    DROP TABLE escalated_agent_skill;

                    ALTER TABLE escalated_agent_skill_par RENAME TO escalated_agent_skill;

                    CREATE UNIQUE INDEX IX_escalated_agent_skill_UserId_SkillId
                        ON escalated_agent_skill (UserId, SkillId);
                    CREATE INDEX IX_escalated_agent_skill_SkillId
                        ON escalated_agent_skill (SkillId);
                    """);
                break;

            case "Npgsql.EntityFrameworkCore.PostgreSQL":
                migrationBuilder.Sql(
                    """

                    ALTER TABLE escalated_agent_skill DROP CONSTRAINT IF EXISTS "FK_escalated_agent_skill_escalated_skills_SkillId";
                    DROP INDEX IF EXISTS "IX_escalated_agent_skill_SkillId";
                    DROP INDEX IF EXISTS "IX_escalated_agent_skill_UserId_SkillId";

                    ALTER TABLE escalated_agent_skill RENAME TO escalated_agent_skill_legacy;

                    CREATE TABLE escalated_agent_skill (
                        "Id" SERIAL CONSTRAINT "PK_escalated_agent_skill" PRIMARY KEY,
                        "UserId" character varying(255) NOT NULL,
                        "SkillId" INTEGER NOT NULL,
                        "Proficiency" INTEGER NOT NULL DEFAULT 3,
                        CONSTRAINT "CK_escalated_agent_skill_proficiency" CHECK ("Proficiency" BETWEEN 1 AND 5),
                        "CreatedAt" TIMESTAMPTZ NOT NULL,
                        "UpdatedAt" TIMESTAMPTZ NOT NULL,
                        CONSTRAINT "FK_escalated_agent_skill_escalated_skills_SkillId"
                            FOREIGN KEY ("SkillId") REFERENCES escalated_skills ("Id") ON DELETE CASCADE
                    );

                    INSERT INTO escalated_agent_skill ("UserId", "SkillId", "Proficiency", "CreatedAt", "UpdatedAt")
                    SELECT
                        "UserId",
                        "SkillId",
                        CASE lower(trim(cast("Proficiency" AS text)))
                            WHEN 'beginner' THEN 1
                            WHEN 'intermediate' THEN 3
                            WHEN 'expert' THEN 5
                            ELSE 3 END,
                        timezone('utc', now()),
                        timezone('utc', now())
                    FROM escalated_agent_skill_legacy;

                    DROP TABLE escalated_agent_skill_legacy;

                    CREATE INDEX "IX_escalated_agent_skill_SkillId" ON escalated_agent_skill ("SkillId");
                    CREATE UNIQUE INDEX "IX_escalated_agent_skill_UserId_SkillId"
                        ON escalated_agent_skill ("UserId","SkillId");
                    """);
                break;

            case "Microsoft.EntityFrameworkCore.SqlServer":
                migrationBuilder.Sql(
                    """

                    IF OBJECT_ID(N'dbo.escalated_agent_skill', N'U') IS NULL RETURN;

                    IF OBJECT_ID(N'FK_escalated_agent_skill_escalated_skills_SkillId', N'F') IS NOT NULL
                        ALTER TABLE dbo.escalated_agent_skill DROP CONSTRAINT [FK_escalated_agent_skill_escalated_skills_SkillId];

                    EXEC sp_rename 'dbo.escalated_agent_skill', 'escalated_agent_skill_legacy';

                    CREATE TABLE dbo.escalated_agent_skill (
                        Id INT IDENTITY CONSTRAINT PK_escalated_agent_skill PRIMARY KEY,
                        UserId NVARCHAR(255) NOT NULL,
                        SkillId INT NOT NULL,
                        Proficiency INT NOT NULL CONSTRAINT CK_escalated_agent_skill_proficiency
                            CHECK (Proficiency BETWEEN 1 AND 5),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_esc_C DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_esc_U DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_escalated_agent_skill_escalated_skills_SkillId FOREIGN KEY(SkillId)
                            REFERENCES dbo.escalated_skills(Id) ON DELETE CASCADE);

                    INSERT INTO dbo.escalated_agent_skill (UserId,SkillId,Proficiency,CreatedAt,UpdatedAt)
                    SELECT
                        UserId,
                        SkillId,
                        CASE WHEN TRY_CONVERT(int, Proficiency) BETWEEN 1 AND 5 THEN TRY_CONVERT(int, Proficiency)
                             ELSE CASE lower(ltrim(rtrim(CONVERT(NVARCHAR(64), Proficiency))))
                                     WHEN N'beginner' THEN 1
                                     WHEN N'intermediate' THEN 3
                                     WHEN N'expert' THEN 5 ELSE 3 END END,
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME()
                    FROM dbo.escalated_agent_skill_legacy;

                    DROP TABLE dbo.escalated_agent_skill_legacy;

                    ALTER TABLE dbo.escalated_agent_skill DROP CONSTRAINT DF_esc_C;
                    ALTER TABLE dbo.escalated_agent_skill DROP CONSTRAINT DF_esc_U;

                    CREATE UNIQUE INDEX IX_escalated_agent_skill_UserId_SkillId
                        ON dbo.escalated_agent_skill(UserId,SkillId);
                    CREATE INDEX IX_escalated_agent_skill_SkillId ON dbo.escalated_agent_skill(SkillId);
                    """);
                break;

            default:
                throw new NotSupportedException(
                    $"Skills parity migration reshapes agent_skill for Sqlite / Npgsql / SqlServer only. Actual provider: '{migrationBuilder.ActiveProvider}'.");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "escalated_skill_routing_departments");
        migrationBuilder.DropTable(name: "escalated_skill_routing_tags");
        // AgentSkill reshape is not reversible from code — rollback requires a bespoke SQL restore (#58).
    }
}
