FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/TokenObservability.Contracts/TokenObservability.Contracts.csproj src/TokenObservability.Contracts/
COPY src/TokenObservability.Domain/TokenObservability.Domain.csproj src/TokenObservability.Domain/
COPY src/TokenObservability.Infrastructure/TokenObservability.Infrastructure.csproj src/TokenObservability.Infrastructure/
COPY src/TokenObservability.Api/TokenObservability.Api.csproj src/TokenObservability.Api/
RUN dotnet restore src/TokenObservability.Api/TokenObservability.Api.csproj

COPY src/ src/
RUN dotnet publish src/TokenObservability.Api/TokenObservability.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TokenObservability.Api.dll"]
