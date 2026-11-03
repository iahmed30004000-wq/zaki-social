FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY Directory.Build.props ZakiSocial.sln ./
COPY src/ZakiSocial.Api/ZakiSocial.Api.csproj src/ZakiSocial.Api/
COPY src/ZakiSocial.Core/ZakiSocial.Core.csproj src/ZakiSocial.Core/
COPY src/ZakiSocial.Data/ZakiSocial.Data.csproj src/ZakiSocial.Data/
COPY src/ZakiSocial.Integrations/ZakiSocial.Integrations.csproj src/ZakiSocial.Integrations/
COPY src/ZakiSocial.Infrastructure/ZakiSocial.Infrastructure.csproj src/ZakiSocial.Infrastructure/
COPY src/ZakiSocial.Workers/ZakiSocial.Workers.csproj src/ZakiSocial.Workers/
RUN dotnet restore ZakiSocial.sln
COPY src ./src
RUN dotnet publish src/ZakiSocial.Api/ZakiSocial.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
LABEL org.opencontainers.image.source="https://github.com/iahmed30004000-wq/zaki-social"
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
COPY --from=build /out ./
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/api/health > /dev/null || exit 1
ENTRYPOINT ["dotnet", "ZakiSocial.Api.dll"]
