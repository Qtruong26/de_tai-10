FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY ["src/AuctionSystem.Shared/AuctionSystem.Shared.csproj", "src/AuctionSystem.Shared/"]
COPY ["src/AuctionSystem.BidValidationService/AuctionSystem.BidValidationService.csproj", "src/AuctionSystem.BidValidationService/"]
RUN dotnet restore "src/AuctionSystem.BidValidationService/AuctionSystem.BidValidationService.csproj"

COPY . .
WORKDIR "/app/src/AuctionSystem.BidValidationService"
RUN dotnet publish "AuctionSystem.BidValidationService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001
ENTRYPOINT ["dotnet", "AuctionSystem.BidValidationService.dll"]

