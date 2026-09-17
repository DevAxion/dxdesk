# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore MillaRemote.Relay.csproj \
 && dotnet publish MillaRemote.Relay.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 5100
ENV ASPNETCORE_URLS=http://0.0.0.0:5100 \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "MillaRemote.Relay.dll"]
