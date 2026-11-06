FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY ["src/AuctionSystem.Shared/AuctionSystem.Shared.csproj", "src/AuctionSystem.Shared/"]
COPY ["src/AuctionSystem.AuctionApi/AuctionSystem.AuctionApi.csproj", "src/AuctionSystem.AuctionApi/"]
RUN dotnet restore "src/AuctionSystem.AuctionApi/AuctionSystem.AuctionApi.csproj"

COPY . .
WORKDIR "/app/src/AuctionSystem.AuctionApi"
RUN dotnet publish "AuctionSystem.AuctionApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
RUN mkdir -p /app/data
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "AuctionSystem.AuctionApi.dll"]

