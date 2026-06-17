FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/TokenObservability.Contracts/TokenObservability.Contracts.csproj src/TokenObservability.Contracts/
COPY src/TokenObservability.Domain/TokenObservability.Domain.csproj src/TokenObservability.Domain/
COPY src/TokenObservability.Infrastructure/TokenObservability.Infrastructure.csproj src/TokenObservability.Infrastructure/
COPY src/TokenObservability.Jobs/TokenObservability.Jobs.csproj src/TokenObservability.Jobs/
RUN dotnet restore src/TokenObservability.Jobs/TokenObservability.Jobs.csproj

COPY src/ src/
RUN dotnet publish src/TokenObservability.Jobs/TokenObservability.Jobs.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TokenObservability.Jobs.dll"]
