# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# S3-01（ENG-02）：基础镜像钉死具体补丁版本，不再拉 latest / 10.0 浮动标签。
# 升级补丁时只改这里两行；digest 钉死需 docker buildx imagetools inspect
# 查当前 digest，见 docs/release.md（本机无 Docker 未在此验证）。
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
ARG BUILD_CONFIGURATION=Release
# S3-01：发布版本号由 Git 标签传入（release 工作流传 VERSION=<tag 去掉 v>）；
# 未传入时按 Directory.Build.props 默认 0.0.0-dev。
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY ["AnyDrop/AnyDrop.csproj", "AnyDrop/"]
RUN dotnet restore "./AnyDrop/AnyDrop.csproj"
COPY . .
WORKDIR "/src/AnyDrop"
RUN dotnet build "./AnyDrop.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0-dev
RUN dotnet publish "./AnyDrop.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$VERSION

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM base AS final
# 重新声明 APP_UID，确保 RUN 中可用（默认 1654，与基础镜像一致）
ARG APP_UID=1654
# 安装需要 root：切换回来执行 apt-get，安装完再切回非特权用户
USER root
# 安装 ffmpeg（视频缩略图）与 curl（供 compose healthcheck 使用：
# aspnet 基础镜像既不带 wget 也不带 curl，原 healthcheck 必然失败），
# 以及 sqlite3（S1-06：备份脚本在容器内用 .backup 给数据库拍在线快照，
# 不再依赖临时 apk add）。
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl sqlite3 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data
USER $APP_UID
WORKDIR /app
COPY --from=publish /app/publish .
# S1-06：备份/恢复脚本随镜像发布，备份可在一次性容器里直接跑
# （docker run --rm -v anydrop-data:/data <image> sh /scripts/backup.sh …）。
COPY scripts/ /scripts/
ENTRYPOINT ["dotnet", "AnyDrop.dll"]