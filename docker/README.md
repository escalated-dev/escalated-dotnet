# Escalated .NET — Docker demo (scaffold, not end-to-end)

Draft. The .NET package is a library; the demo needs its own minimal ASP.NET Core host (Program.cs + appsettings + EF Core migrations) that references the library via ProjectReference to `src/Escalated/Escalated.csproj`. Not scaffolded yet. Current Dockerfile attempts `dotnet publish` on the library alone which will not produce an entrypoint.
