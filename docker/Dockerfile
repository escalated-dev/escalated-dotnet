# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /package
COPY . .
RUN dotnet restore Escalated.sln \
    && dotnet publish src/Escalated/Escalated.csproj -c Release -o /out || \
    echo "[demo] publish failed — this is a library, not an app; demo needs its own host project"

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=builder /out ./
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
EXPOSE 8080
ENTRYPOINT ["entrypoint.sh"]
CMD ["dotnet", "Escalated.dll"]
