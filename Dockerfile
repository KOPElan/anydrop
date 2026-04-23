# ─── 阶段 1：Tailwind CSS 构建 ──────────────────────────────────────────────────
FROM node:22-alpine AS css-build
WORKDIR /src

# 只复制 CSS 构建所需文件，最大化层缓存命中率
COPY package.json ./
RUN npm install

COPY AnyDrop/wwwroot/app.css AnyDrop/wwwroot/app.css
# 复制 Razor 文件供 Tailwind 扫描类名（避免生产包缺少用到的样式）
COPY AnyDrop/Components/ AnyDrop/Components/

RUN npx @tailwindcss/cli \
        -i ./AnyDrop/wwwroot/app.css \
        -o ./AnyDrop/wwwroot/tailwind.css \
        --minify

# ─── 阶段 2：.NET 构建与发布 ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 复制解决方案与项目文件（利用层缓存，依赖变化前不重新 restore）
COPY AnyDrop.slnx ./
COPY AnyDrop/AnyDrop.csproj AnyDrop/

RUN dotnet restore AnyDrop/AnyDrop.csproj

# 复制源码，并从 css-build 阶段注入已构建好的 tailwind.css
COPY AnyDrop/ AnyDrop/
COPY --from=css-build /src/AnyDrop/wwwroot/tailwind.css AnyDrop/wwwroot/tailwind.css

RUN dotnet publish AnyDrop/AnyDrop.csproj \
        -c Release \
        -o /publish \
        --no-restore

# ─── 阶段 2：运行时镜像 ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# 创建非 root 运行用户
RUN addgroup -S anydrop && adduser -S anydrop -G anydrop

# 持久化数据目录（SQLite 数据库 + 上传文件）
RUN mkdir -p /data/files && chown -R anydrop:anydrop /data

COPY --from=build --chown=anydrop:anydrop /publish ./

USER anydrop

# 数据目录由外部 volume 挂载覆盖
VOLUME ["/data"]

# Kestrel 监听 8080，避免以 root 监听特权端口
ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__DatabasePath=/data/anydrop.db
ENV Storage__BasePath=/data/files

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -qO- http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "AnyDrop.dll"]
