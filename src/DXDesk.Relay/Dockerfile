# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore DXDesk.Relay.csproj \
 && dotnet publish DXDesk.Relay.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./

# Audit və sertifikat qovluqları məhdud "app" istifadəçisinə aid olsun
# (volume bağlananda hostdakı qovluğun sahibi istifadə olunur — README-yə baxın).
RUN mkdir -p /app/audit /app/certs && chown -R app:app /app/audit

EXPOSE 5100
ENV ASPNETCORE_URLS=http://0.0.0.0:5100 \
    ASPNETCORE_ENVIRONMENT=Production

# TƏHLÜKƏSİZLİK: root kimi yox, image-dəki məhdud "app" istifadəçisi (uid 1654) kimi işlə.
# 5100 portu 1024-dən böyük olduğu üçün xüsusi imtiyaz lazım deyil.
USER $APP_UID

ENTRYPOINT ["dotnet", "DXDesk.Relay.dll"]
