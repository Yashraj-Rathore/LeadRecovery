FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props LeadRecovery.sln ./
COPY src/LeadRecovery.Application/*.csproj src/LeadRecovery.Application/packages.lock.json src/LeadRecovery.Application/
COPY src/LeadRecovery.Contracts/*.csproj src/LeadRecovery.Contracts/packages.lock.json src/LeadRecovery.Contracts/
COPY src/LeadRecovery.Domain/*.csproj src/LeadRecovery.Domain/packages.lock.json src/LeadRecovery.Domain/
COPY src/LeadRecovery.Infrastructure/*.csproj src/LeadRecovery.Infrastructure/packages.lock.json src/LeadRecovery.Infrastructure/
COPY src/LeadRecovery.Worker/*.csproj src/LeadRecovery.Worker/packages.lock.json src/LeadRecovery.Worker/
RUN dotnet restore src/LeadRecovery.Worker/LeadRecovery.Worker.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/LeadRecovery.Worker/LeadRecovery.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10@sha256:1fa23fc4872d95fd71c2833ebe65d7e84a43b2d51a31d119516852f13d9505a7 AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
ARG CREATED=unknown
LABEL org.opencontainers.image.title="LeadRecovery Worker" \
      org.opencontainers.image.description="LeadRecovery background job processor" \
      org.opencontainers.image.source="https://github.com/Yashraj-Rathore/LeadRecovery" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.created="$CREATED"

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/live HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3 && read -r status <&3 && [[ "$status" == *" 200 "* ]]'
ENTRYPOINT ["dotnet", "LeadRecovery.Worker.dll"]
