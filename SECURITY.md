# Security policy

## Supported version

The maintained release is **1.4.4**. Upgrade earlier versions to receive the storage, updater and WebView fixes.

## Trust boundaries

- Native WebView2 message handlers validate the document origin and message type.
- Portable updates require SHA-256 and reject a known size mismatch. Digests come from GitHub asset metadata or a matching checksum sidecar. GitHub release ownership remains a trust boundary.
- Archives are extracted to a new staging directory with traversal and expanded-size checks. The updater backs up application files before replacement and restores them if copying fails.
- Watch Together, its TCP listener and firewall management have been removed.
- DPAPI encryption is scoped to the Windows user. It does not isolate files from processes running as the same user and is not server-authoritative anti-cheat. Legacy formats remain readable for migration.
- Atomic writes and backups protect data against interrupted saves. Unrecoverable errors preserve originals instead of silently resetting the profile.
- One application instance per Windows session prevents competing profile writers.

## Reporting

Use private vulnerability reporting on GitHub when available. Do not attach credentials, browser profile files or unredacted private logs to public issues. For ordinary bugs, use [Issues](https://github.com/dragloner/JokerDBDTracker/issues) with the app/Windows versions, reproduction steps and expected/actual results.
