# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /src
COPY src ./src
COPY docker/host-app ./docker/host-app
WORKDIR /src/docker/host-app
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=builder /out ./
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV APP_ENV=demo
ENTRYPOINT ["entrypoint.sh"]
CMD ["dotnet", "EscalatedDemoHost.dll"]
