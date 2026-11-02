FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/gateway/Commerce.Gateway/ src/gateway/Commerce.Gateway/
RUN dotnet restore src/gateway/Commerce.Gateway --locked-mode
RUN dotnet publish src/gateway/Commerce.Gateway -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Commerce.Gateway.dll"]
