FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/services/identity/Commerce.Identity.Api/ src/services/identity/Commerce.Identity.Api/
COPY src/services/identity/Commerce.Identity.Contracts/ src/services/identity/Commerce.Identity.Contracts/
COPY src/services/identity/Commerce.Identity.Application/ src/services/identity/Commerce.Identity.Application/
COPY src/services/identity/Commerce.Identity.Infrastructure/ src/services/identity/Commerce.Identity.Infrastructure/
RUN dotnet restore src/services/identity/Commerce.Identity.Api --locked-mode
RUN dotnet publish src/services/identity/Commerce.Identity.Api -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Commerce.Identity.Api.dll"]
