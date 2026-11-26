FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS source
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ src/

FROM source AS api-build
RUN dotnet restore src/SinalFrio.Api/SinalFrio.Api.csproj --locked-mode \
    && dotnet publish src/SinalFrio.Api/SinalFrio.Api.csproj -c Release --no-restore -o /publish

FROM source AS worker-build
RUN dotnet restore src/SinalFrio.Worker/SinalFrio.Worker.csproj --locked-mode \
    && dotnet publish src/SinalFrio.Worker/SinalFrio.Worker.csproj -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS api
WORKDIR /app
COPY --from=api-build /publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "SinalFrio.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS worker
WORKDIR /app
COPY --from=worker-build /publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "SinalFrio.Worker.dll"]
