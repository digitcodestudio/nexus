# Security Policy — NEXUS System Optimizer

## Supported Versions
| Version | Supported |
| :--- | :---: |
| `1.x` (Latest Release) | Yes |

## Security Architecture Commitments
- **Least Privilege:** Normal monitoring runs unelevated (`asInvoker`).
- **Immutable OS Guards:** Critical Windows services and core OS processes cannot be disabled or terminated by automation.
- **Restricted IPC:** Local Named Pipes enforce strict Windows SID ACLs (`CurrentUser`, `BUILTIN\Administrators`, `NT AUTHORITY\SYSTEM`).
- **Mandatory Rollback:** Every Windows Service modification creates a pre-change snapshot in SQLite.

## Reporting a Vulnerability
If you discover a security vulnerability, privilege escalation issue, or unsafe Windows API behavior:
1. **Do NOT** open a public GitHub issue with exploit details.
2. Report privately via GitHub Security Advisories (`Security → Advisories → Report a vulnerability`) in `digitcodestudio/nexus`.
3. Include reproduction steps, affected Windows build, and suggested mitigation.

