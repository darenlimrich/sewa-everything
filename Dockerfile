FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SewaEverything.Domain/SewaEverything.Domain.csproj src/SewaEverything.Domain/
COPY src/SewaEverything.Contracts/SewaEverything.Contracts.csproj src/SewaEverything.Contracts/
COPY src/SewaEverything.Infrastructure/SewaEverything.Infrastructure.csproj src/SewaEverything.Infrastructure/
COPY src/SewaEverything.Api/SewaEverything.Api.csproj src/SewaEverything.Api/
RUN dotnet restore src/SewaEverything.Api/SewaEverything.Api.csproj

COPY src/SewaEverything.Domain/ src/SewaEverything.Domain/
COPY src/SewaEverything.Contracts/ src/SewaEverything.Contracts/
COPY src/SewaEverything.Infrastructure/ src/SewaEverything.Infrastructure/
COPY src/SewaEverything.Api/ src/SewaEverything.Api/
RUN rm -f src/SewaEverything.Api/appsettings.Development.json \
 && rm -rf src/SewaEverything.Api/storage \
 && dotnet publish src/SewaEverything.Api/SewaEverything.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    Storage__Photos__Provider=database \
    Security__TrustProxyHeaders=true \
    Security__TrustAllProxies=true

EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "SewaEverything.Api.dll"]
