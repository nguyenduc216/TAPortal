# TAPortal Database Plugin

## Goal
Package the TAPortal database skill together with the remote read-only MCP server so supported Codex/Desktop surfaces can inspect the live SQL Server database safely.

## Files

```text
TAPortal/
├─ .codex-plugin/
│  └─ plugin.json
├─ .mcp.json
└─ .skills/
   └─ taportal-database/
      ├─ SKILL.md
      ├─ examples.md
      └─ tests.md
```

## Remote MCP

```text
https://mcp.tuvangiaiphapta.vn/mcp
```

The MCP server is intentionally read-only and should use SQL login `taportal_ai_reader`.

## What the plugin enables
- connectivity check (`DbPing`)
- list schemas/tables
- inspect table schema, foreign keys, indexes and constraints
- inspect views/stored procedures
- get a schema snapshot
- execute controlled read-only SQL through `DbQueryReadOnly`

## Security boundary
The plugin must not be used as a raw database write channel. Writes should be implemented later as explicit TAPortal application commands with validation, authorization and audit logging.

## Install/use expectation
Imported plugins that declare MCP servers may be available only on supported Desktop/Codex surfaces and may be marked `Desktop only`. ChatGPT web availability depends on plan/product support. The repository package therefore targets Codex/Desktop first while keeping the skill portable for future ChatGPT plugin/app availability.

## Smoke-test order
1. Invoke the plugin and ask it to check TAPortal DB connectivity.
2. Confirm `DbPing` succeeds.
3. Ask for the schema of `dbo.Users`.
4. Ask for a list of common tables.
5. Ask for the number of roles/permissions/data scopes.
6. Run the acceptance cases in `.skills/taportal-database/tests.md`.

## Development rule
Before building a new backend/UI function, use this plugin to inspect the actual database first. Database changes must be committed as reviewed migrations, then verified through the read-only MCP.
