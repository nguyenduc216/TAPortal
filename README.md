# TAPortal

T.A Portal - nền tảng quản lý dịch vụ kế toán, xây dựng trên .NET 8 và SQL Server.

## Repository structure

```text
TAPortal/
├── src/
│   ├── TAPortal.Web/
│   ├── TAPortal.Application/
│   ├── TAPortal.Domain/
│   ├── TAPortal.Infrastructure/
│   ├── TAPortal.Shared/
│   └── TAPortal.DbMcp/
├── database/
│   ├── migrations/
│   ├── seeds/
│   ├── scripts/
│   └── diagrams/
├── docs/
├── .skills/
└── TAPortal.sln
```

## MCP database service

`src/TAPortal.DbMcp` là MCP server .NET 8 dùng để cho AI đọc cấu trúc SQL Server có kiểm soát.

Endpoints:

```text
GET /health
MCP /mcp
```

Read-only tools hiện có:

```text
DbPing
DbListSchemas
DbListTables
DbGetTableSchema
DbGetForeignKeys
DbGetIndexes
DbGetConstraints
DbGetViews
DbGetStoredProcedures
DbGetSchemaSnapshot
DbQueryReadOnly
```

`DbQueryReadOnly` chỉ cho phép một câu SELECT/CTE và chặn DDL/DML/procedure execution.

## Local build

```powershell
dotnet restore TAPortal.sln
dotnet build TAPortal.sln -c Release
```

Run MCP:

```powershell
$env:Database__ConnectionString = "Server=<sql-host>,1433;Database=TAPortal;User Id=taportal_ai_reader;Password=<secret>;Encrypt=True;TrustServerCertificate=False"
dotnet run --project src/TAPortal.DbMcp/TAPortal.DbMcp.csproj --urls http://127.0.0.1:5100
```

Health check:

```text
http://127.0.0.1:5100/health
```

## SQL account

Run `database/scripts/001-create-mcp-readonly-user.sql` manually as a SQL administrator after replacing the placeholder password. Never commit a real password.

## Recommended production topology

```text
https://portal.example.vn -> TAPortal.Web
https://mcp.example.vn    -> TAPortal.DbMcp -> SQL Server/TAPortal
```

Both services may run on the same server/public IP using separate internal ports and IIS/Nginx reverse proxy.

Before exposing `/mcp` publicly, add authentication, TLS, rate limiting and request audit logging. See `docs/mcp-deployment.md`.

## CI

GitHub Actions `.github/workflows/build.yml` restores, builds and publishes the MCP project on pushes and pull requests.
