# Legacy TAPortal migrations

Migrations `001`–`052` under `database/migrations/` are the audited legacy `TAPortal` V1 chain. They contain historical `USE [TAPortal]` boundaries and must not be executed against `TAPortalSys`.

TAPortalSys additive migrations begin with `053`. A navigation seed identified operationally as migration `054` was reportedly executed outside this repository, but its exact verified source was not available during `TAPORTAL-20261010-HR-MASTER-DATA-001`. Number `054` is therefore reserved and must not be reused for different content. Department master starts at `055`.

Recommended follow-up: recover the exact executed 054 artifact and hash from deployment evidence, commit it unchanged, then establish a TAPortalSys manifest/hash gate. Moving 001–052 into this folder should be a dedicated cleanup change only after all deployment tooling references are inventoried; this marker deliberately does not move or delete historical files.
