# Security policy

## What the add-on exposes

The add-on starts an MCP server **inside `CorelDRAW.exe`** and binds it to the loopback
interface (`127.0.0.1`, port `8765` by default). It is not reachable from the network.

Anyone who can reach that endpoint can drive the running CorelDRAW session with the full
privileges of the application — create, modify and delete drawings, read and write files through
the import/export and document tools, and start or stop the server itself. Treat the endpoint as
equivalent to an unlocked desktop session.

Defaults that matter:

| Setting | Default | Effect |
|---|---|---|
| `BindAddress` | `127.0.0.1` | loopback only |
| `AuthToken` | empty | **no authentication on the endpoint** |
| `AllowedRoots` | empty | **no restriction** on file paths passed to tools |
| `MaxBodyBytes` | 4 MiB | upper bound on request size |

## If you need more than loopback

1. Set `AuthToken` in `config.json` — requests must then send `Authorization: Bearer <token>`.
2. Set `AllowedRoots` to the directories file-path arguments may point at.
3. Put the endpoint behind a tunnel or reverse proxy you control. Do **not** simply change
   `BindAddress` to `0.0.0.0` without steps 1 and 2.

## Supported versions

Fixes are made on the current `main`; the add-on targets CorelDRAW Graphics Suite 2025 (26) and
2026 (27). Older CorelDRAW releases are out of scope.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

- Preferred: open a private report through **Security ▸ Report a vulnerability** on this
  repository (GitHub Security Advisories).
- If advisories are not enabled, contact the maintainer listed in `LICENSE` and say so in a
  short public issue without details.

Include: the add-on version (`server_status` → `version`), your CorelDRAW release, the request
or tool call involved, and what an attacker gains. A `corelmcp.log` excerpt helps.

You will get an acknowledgement, an assessment, and — if the report holds — a fix and a note in
`CHANGELOG.md`. Please allow reasonable time for a release before disclosing publicly.
