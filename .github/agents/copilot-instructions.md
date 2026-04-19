# anydrop Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-19

## Active Technologies
- C# 13 / .NET 10 + Blazor Server (Interactive Server), EF Core 10.x + SQLite, SignalR (built-in), Tailwind CSS v4 (CLI build), HtmlAgilityPack, SkiaSharp (003-rich-media-chat)
- C# 13 / .NET 10 + Blazor Server (Interactive Server), EF Core 10.x + SQLite, SignalR (built-in), Tailwind CSS v4 (CLI build) (main)
- SQLite via EF Core；文件路径从 `Storage:DatabasePath` 配置读取，默认 `./data/anydrop.db` (main)
- C# 13 / .NET 10 + Blazor Server (Interactive Server), EF Core 10 + SQLite, SignalR, SortableJS (CDN), Tailwind CSS v4 (speckit/002-topic-session-sidebar)
- SQLite（通过 EF Core，新增 `Topics` 表，`ShareItems` 表新增 `TopicId` 列） (speckit/002-topic-session-sidebar)
- [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION] (main)
- [if applicable, e.g., PostgreSQL, CoreData, files or N/A] (main)

- C# 13 / .NET 10.0 + Microsoft.FluentUI.AspNetCore.Components 4.13.2、Microsoft.EntityFrameworkCore.Sqlite（10.x）、Microsoft.AspNetCore.OpenApi（.NET 10 内置）、Scalar.AspNetCore（Swagger UI） (main)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# 13 / .NET 10.0

## Code Style

C# 13 / .NET 10.0: Follow standard conventions

## Recent Changes
- 003-rich-media-chat: Added HtmlAgilityPack (链接 meta 解析) + SkiaSharp (图片缩略图) + IHostedService+Channel<T> 后台任务队列 + LinkPreview/MediaMetadata 新实体 + 文件上传异步状态机
- main: Added [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]
- speckit/002-topic-session-sidebar: Added C# 13 / .NET 10 + Blazor Server (Interactive Server), EF Core 10 + SQLite, SignalR, SortableJS (CDN), Tailwind CSS v4
- main: Added C# 13 / .NET 10 + Blazor Server (Interactive Server), EF Core 10.x + SQLite, SignalR (built-in), Tailwind CSS v4 (CLI build)


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
