# Releasing `Escalated` to NuGet

Publishing a GitHub release runs `.github/workflows/publish.yml`, which pushes
the [`Escalated`](https://www.nuget.org/packages/Escalated) package to nuget.org.

## What the workflow does

1. Checks out the release tag (`vX.Y.Z`).
2. Fails if `<Version>` in `src/Escalated/Escalated.csproj` is not `X.Y.Z`.
3. Restores, builds and runs the test suite in `Release`.
4. `dotnet pack` builds `Escalated.X.Y.Z.nupkg`, lists its contents in the log
   and uploads it as a workflow artifact.
5. `dotnet nuget push` sends it to nuget.org (`--skip-duplicate`, so a re-run
   of an already-published version is a no-op).

If `NUGET_API_KEY` is not set, step 5 logs
`skipping: NUGET_API_KEY not configured` and the job still succeeds; steps 1-4
still run, so every release is at least build-, test- and pack-checked.

## Required secret

Add in **Settings -> Secrets and variables -> Actions** (repo or org level):

| Secret | Value |
|---|---|
| `NUGET_API_KEY` | API key from <https://www.nuget.org/account/apikeys>, scope **Push new packages and package versions**, glob `Escalated` (or `Escalated*` to also cover `Escalated.Locale`). |

`Escalated` is a new package ID, so the first push claims it for the account
that owns the key.

## Cutting a release

1. Bump `<Version>` in `src/Escalated/Escalated.csproj` and update
   `CHANGELOG.md`; merge to `main`.
2. Create the release: `gh release create vX.Y.Z --generate-notes`
   (or via the GitHub UI). Publishing the release triggers the workflow.

## Re-running for an existing tag

A release published before the secret was added (for example `v0.2.1`) can be
published afterwards without a new tag:

```bash
gh workflow run publish.yml -f tag=v0.2.1
gh run watch "$(gh run list --workflow publish.yml --limit 1 --json databaseId -q '.[0].databaseId')"
```

Or **Actions -> Publish -> Run workflow**, tag `v0.2.1`. The workflow
definition comes from `main`; the package is built from the tag.

## Dependencies

All runtime dependencies resolve from nuget.org. The translation catalog from
`escalated-dev/escalated-locale` is vendored as embedded resources
(`Resources/locales/*.json`), so the package does not depend on
`Escalated.Locale` being published; installing that package is optional.
