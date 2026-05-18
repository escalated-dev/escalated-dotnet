# Cursor task: skills-management parity for escalated-dotnet

Read this whole file before doing anything. Self-contained brief.

## Goal

Bring `escalated-dotnet` to feature parity with the canonical Skills-management contract.

**Tracking issue:** https://github.com/escalated-dev/escalated-dotnet/issues/58
**Canonical contract:** https://github.com/escalated-dev/escalated-developer-context/blob/main/domain-model/skills-management.md
**ADR:** https://github.com/escalated-dev/escalated-developer-context/blob/main/decisions/2026-05-13-skills-routing-explicit-mapping.md
**NestJS reference (study):** https://github.com/escalated-dev/escalated-nestjs/pull/45
**Frontend contract:** https://github.com/escalated-dev/escalated/pull/65

## Current state

- `src/Escalated/Models/Skill.cs`: has `Id, Name, Slug, Description, CreatedAt, UpdatedAt, AgentSkills` collection.
- `src/Escalated/Models/Skill.cs` (same file): has `AgentSkill { UserId, SkillId, Proficiency (string!) }` — **proficiency is a string** ("beginner|intermediate|expert"). **Contract requires int 1..5** — needs migration to `int`.
- `src/Escalated/Services/SkillRoutingService.cs`: exists, uses older routing logic — needs replacement.
- **No** admin controller, no admin UI, no routing-tag/department tables.

## Deliverables

1. **EF Core migrations** (in the project's `Migrations/` dir):
   - Add `EscalatedSkillRoutingTags` (Id, SkillId FK cascade, TagId FK cascade, unique index on (SkillId, TagId)).
   - Add `EscalatedSkillRoutingDepartments` (Id, SkillId FK cascade, DepartmentId FK cascade, unique index on (SkillId, DepartmentId)).
   - **Migrate `AgentSkill.Proficiency` from `string` to `int`** with default 3, check constraint 1..5. Backfill: `beginner → 1, intermediate → 3, expert → 5`, anything else → 3.
   - Add `Id` PK to `AgentSkill` if it doesn't have one (its current model shows `UserId` + `SkillId` as composite key — Entity Framework will need explicit configuration).
   - Add timestamps to `AgentSkill` if missing.

2. **Model updates** (`src/Escalated/Models/`):
   - Update `AgentSkill` to use `int Proficiency` with `[Range(1, 5)]`.
   - Add `SkillRoutingTag` and `SkillRoutingDepartment` entity classes.
   - On `Skill`, add navigation collections for `RoutingTags` and `RoutingDepartments`.
   - Update `EscalatedDbContext` (`OnModelCreating`) to configure the new entities, their unique indices, and the cascade behaviour.

3. **DTOs** (`src/Escalated/Dtos/Admin/`):
   - `CreateSkillDto` / `UpdateSkillDto` with `Name`, `Description?`, `RoutingTagIds: int[]?`, `RoutingDepartmentIds: int[]?`, `Agents: AgentSkillEntryDto[]?`.
   - `AgentSkillEntryDto`: `UserId: int`, `Proficiency: int` (1..5).
   - Use `System.ComponentModel.DataAnnotations` or FluentValidation, whichever the rest of the repo uses.

4. **Controller** (`src/Escalated/Controllers/Admin/SkillController.cs` or similar — match existing naming):
   - 6 actions: `Index`, `Create`, `Store`, `Edit`, `Update`, `Destroy`.
   - JSON responses. Index shape: `{ skills: [{ id, name, agentsCount, routingTagsCount, routingDepartmentsCount, updatedAt }] }`.
   - Edit shape: `{ skill: { id, name, description, routingTagIds, routingDepartmentIds, agents }, availableAgents, availableTags, availableDepartments }`.
   - Note: C# convention is camelCase JSON output via `System.Text.Json` defaults — that's fine, matches existing repo style. The frontend contract is snake_case but other plugins serialise that locally; pick whichever the existing controllers do.
   - Wrap writes in a transaction (`await using var transaction = await _context.Database.BeginTransactionAsync()`).

5. **Refactor `SkillRoutingService`**:
   - Required skill ids = skills whose `RoutingTags` overlap ticket tags OR whose `RoutingDepartments` contain the ticket's department.
   - Eligible agents = users with `AgentSkill` rows covering ALL required skills (`GroupBy(UserId).Where(g => g.Select(x => x.SkillId).Distinct().Count() == requiredCount)`).
   - Order by sum of proficiency desc, then existing capacity.

6. **Tests** (`tests/` — adopt the test project that exists; likely `Escalated.Tests` xUnit):
   - Controller integration tests for the 6 actions.
   - Service tests for the explicit-mapping routing.
   - Migration test if the project has one — verify the proficiency conversion from string to int works on existing data.

## Process

1. `git checkout -b feat/admin-skills-management`.
2. Read the contract + NestJS PR diff before coding.
3. Implement: migrations → models → DTOs → controller → service → tests.
4. Run: `dotnet build`, `dotnet test`, format (`dotnet format`).
5. Commit logically, reference #58.
6. Push, open PR titled `feat(skills): admin skills management parity (#58)`.

## Constraints

- Don't break the existing `Proficiency` string usage callers — if any code reads `Proficiency` as a string, update it to the new int. Grep for `Proficiency` to find them.
- Wrap multi-table writes in a transaction.
- Stop after pushing the PR.
- The PROMPT file you're reading is untracked — do not include it in the PR.

## Self-check before pushing

- `dotnet build` clean
- `dotnet test` all green
- `dotnet format --verify-no-changes` clean
- `git log --oneline` shows your commits
- The Proficiency type migration round-trip works (test or manual SQL spot-check)
