# syntax=docker/dockerfile:1

# ---- front -------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM oven/bun:1-alpine AS web
WORKDIR /repo/web
COPY web/package.json web/bun.lock ./
RUN bun install --frozen-lockfile
COPY web/ ./
# vite.config.ts writes into ../src/Bigarr.Api/wwwroot
RUN bun run build

# ---- backend -----------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /repo
COPY global.json Directory.Build.props Bigarr.slnx ./
COPY src/Bigarr.Domain/Bigarr.Domain.csproj src/Bigarr.Domain/
COPY src/Bigarr.Parsing/Bigarr.Parsing.csproj src/Bigarr.Parsing/
COPY src/Bigarr.Decision/Bigarr.Decision.csproj src/Bigarr.Decision/
COPY src/Bigarr.Api/Bigarr.Api.csproj src/Bigarr.Api/
RUN dotnet restore src/Bigarr.Api -a $TARGETARCH
COPY src/ src/
COPY --from=web /repo/src/Bigarr.Api/wwwroot src/Bigarr.Api/wwwroot
RUN dotnet publish src/Bigarr.Api -a $TARGETARCH -c Release --no-restore -o /app

# ---- runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
# Full ICU: international titles, culture-aware sorting and string normalization. ffmpeg brings ffprobe, which reads
# codecs, channels, HDR and languages from imported files (blueprint §3.4).
RUN apk add --no-cache icu-libs icu-data-full tzdata ffmpeg
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:7979 \
    Bigarr__ConfigDirectory=/config \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
# One shared data mount for downloads and libraries: hardlinks need it (blueprint §2, §8).
VOLUME ["/config"]
EXPOSE 7979
USER $APP_UID
COPY --from=build --chown=$APP_UID:$APP_UID /app .
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://127.0.0.1:7979/health || exit 1
ENTRYPOINT ["dotnet", "Bigarr.Api.dll"]
