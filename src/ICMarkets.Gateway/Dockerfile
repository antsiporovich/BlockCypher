FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.Config", "./"]
COPY ["src/", "src/"]
RUN dotnet restore "src/ICMarkets.Gateway/ICMarkets.Gateway.csproj"
RUN dotnet publish "src/ICMarkets.Gateway/ICMarkets.Gateway.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "ICMarkets.Gateway.dll"]
