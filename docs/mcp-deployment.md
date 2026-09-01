# TAPortal MCP Deployment

## Recommended topology

```text
Internet
  |
  +-- https://portal.example.vn  -> TAPortal.Web
  |
  +-- https://mcp.example.vn/mcp -> TAPortal.DbMcp
                                      |
                                      +-> SQL Server / TAPortal
```

`TAPortal.Web` và `TAPortal.DbMcp` có thể chạy trên cùng một Windows/Linux server nhưng phải là hai process/service riêng. Có thể dùng cùng public IP; DNS của hai subdomain cùng trỏ về IP đó. Reverse proxy (IIS/Nginx/Apache) định tuyến theo hostname tới port nội bộ khác nhau.

Ví dụ nội bộ:

```text
portal.example.vn -> 127.0.0.1:5000
mcp.example.vn    -> 127.0.0.1:5100
```

MCP endpoint:

```text
https://mcp.example.vn/mcp
```

Health endpoint:

```text
https://mcp.example.vn/health
```

## Required environment variable

Không lưu connection string trong Git.

Windows PowerShell example:

```powershell
$env:Database__ConnectionString = "Server=<sql-host>,1433;Database=TAPortal;User Id=taportal_ai_reader;Password=<secret>;Encrypt=True;TrustServerCertificate=False"
dotnet TAPortal.DbMcp.dll --urls http://127.0.0.1:5100
```

## SQL Server network

Ưu tiên MCP và SQL Server ở cùng private network. Nếu SQL Server dùng public IP, firewall chỉ whitelist IP của server MCP; không mở TCP 1433 cho `0.0.0.0/0`.

## Security before production

Skeleton hiện chỉ cung cấp read-only database tools. Trước khi expose `/mcp` ra Internet phải bổ sung authentication (ưu tiên OAuth/OIDC theo cơ chế hỗ trợ của MCP host), TLS hợp lệ, rate limiting và audit logging.
