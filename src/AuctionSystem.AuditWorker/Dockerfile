FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY ["src/AuctionSystem.Shared/AuctionSystem.Shared.csproj", "src/AuctionSystem.Shared/"]
COPY ["src/AuctionSystem.AuditWorker/AuctionSystem.AuditWorker.csproj", "src/AuctionSystem.AuditWorker/"]
RUN dotnet restore "src/AuctionSystem.AuditWorker/AuctionSystem.AuditWorker.csproj"

COPY . .
WORKDIR "/app/src/AuctionSystem.AuditWorker"
RUN dotnet publish "AuctionSystem.AuditWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
RUN mkdir -p /app/data
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuctionSystem.AuditWorker.dll"]

