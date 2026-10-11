# Phase 7 security remediation register

| Finding | Severity | Owner | Required action / dependency | Verification criteria |
|---|---|---|---|---|
| Plaintext SQL credential in local appsettings and historical publish outputs | CRITICAL | Application/operations | Rotate credential; move to secret store; inventory backups/shares; separately assess Git history | Old credential rejected; repo/publish secret scan clean; app starts from secret injection |
| SQL connection negotiated TLS 1.0 | HIGH | SQL/network | Enable TLS 1.2+, deploy trusted certificate, remove trust bypass after certificate rollout | SqlClient connection has no downgrade/certificate warning |
| Public SQL endpoint | HIGH | Network/security | Restrict by VPN/private network and firewall allow-list | External unapproved source cannot connect; staging/app sources can |
| Missing OperationalDb tables/ownership | HIGH | Architecture/database | Decide correct database or retire obsolete methods; provide migration owner if required | Every OperationalDb query maps to an approved deployed schema and integration test |
| Publish artifacts may contain local configuration | HIGH | Release/operations | Locate ignored/synced/backed-up publish directories, restrict access, regenerate after rotation | Artifact scan has no secret; sharing/retention policy documented |
| No authorized staging accounts/reset fixture | MEDIUM | QA/operations | Provision synthetic permission matrix and deterministic cleanup | Authenticated Playwright and CRUD matrix execute repeatably without production data |

No production security, firewall, SQL Server, credential, or history-rewrite action was performed in this change.
