## What changed

<!-- One or two sentences. The "why" matters more than the "what". -->

## How it was verified

<!-- Which tests ran? Did you try it against a live CorelDRAW, and which release? -->

- [ ] `dotnet build src\CorelMcpAddon\CorelMcpAddon.csproj -c Release -p:CorelDrawVersion=27`
- [ ] `dotnet test src\CorelMcpAddon.Tests\CorelMcpAddon.Tests.csproj`
- [ ] `.\docs\check-docs.ps1`
- [ ] exercised against CorelDRAW (release: ___) — or: not applicable, docs only

## Checklist

- [ ] `README.md` and `README.ru.md` are both updated (same headings, same content)
- [ ] `skills/` updated when a tool name, argument or default changed
- [ ] no tool count, log path or `config.json` table restated outside its owning document
- [ ] no build output committed (`bin/`, `obj/`, `dist/`, `addon/`, `*.wixpdb`)
- [ ] `installer/version.txt` untouched
- [ ] an issue is linked if this fixes one
