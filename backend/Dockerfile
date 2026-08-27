FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY FraudDetection.Api/FraudDetection.Api.csproj FraudDetection.Api/
RUN dotnet restore FraudDetection.Api/FraudDetection.Api.csproj
COPY FraudDetection.Api/ FraudDetection.Api/
RUN dotnet publish FraudDetection.Api/FraudDetection.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
# curl is only here so Docker Compose's healthcheck can hit /health/live -- other services
# (scala-risk-engine, pyspark-analytics) depend on that healthcheck to avoid starting before
# this container has run its EF Core migrations.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=10 \
    CMD curl -f http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "FraudDetection.Api.dll"]
