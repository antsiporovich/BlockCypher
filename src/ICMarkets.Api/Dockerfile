FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.Config", "./"]
COPY ["src/", "src/"]
RUN dotnet restore "src/ICMarkets.Api/ICMarkets.Api.csproj"
RUN dotnet publish "src/ICMarkets.Api/ICMarkets.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ICMarkets.Api.dll"]
