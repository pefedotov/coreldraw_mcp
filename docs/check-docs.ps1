<#
.SYNOPSIS
  Checks that the documentation is internally consistent.

.DESCRIPTION
  Read-only. Fails when the two READMEs drift apart, when a relative link or anchor points at
  nothing, when a tool count is hard-coded in prose, or when the log location is described in a
  way that contradicts the code.

  Run it from anywhere:
    .\docs\check-docs.ps1
    .\docs\check-docs.ps1 -Verbose
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent $PSScriptRoot
$readmeEn = Join-Path $repoRoot 'README.md'
$readmeRu = Join-Path $repoRoot 'README.ru.md'
$toolbox = Join-Path $repoRoot 'docs\tools.md'

$problems = New-Object System.Collections.Generic.List[string]

function Read-Lines([string]$path) {
    if (-not (Test-Path $path)) { throw "Missing file: $path" }
    $text = [System.IO.File]::ReadAllText($path, $utf8)
    # Drop HTML comments first: they hold notes and placeholders, not links.
    $text = [regex]::Replace($text, '(?s)<!--.*?-->', '')
    return $text -split "`r?`n"
}

function Get-Headings([string[]]$lines) {
    $result = New-Object System.Collections.Generic.List[object]
    foreach ($line in $lines) {
        if ($line -match '^(#{1,6})\s+(.*)$') {
            $result.Add([pscustomobject]@{ Level = $Matches[1].Length; Text = $Matches[2].Trim() })
        }
    }
    return $result
}

# GitHub's heading anchor: lowercase, drop everything that is not a letter, digit, space,
# hyphen or underscore (Cyrillic included), then join words with a single hyphen.
function ConvertTo-Anchor([string]$text) {
    $slug = $text.ToLowerInvariant()
    $slug = $slug -replace '`', ''
    $slug = $slug -replace '[^\p{L}\p{Nd} _-]', ''
    $slug = $slug.Trim()
    $slug = $slug -replace '\s+', '-'
    return $slug
}

function Get-Links([string[]]$lines) {
    $links = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        foreach ($match in [regex]::Matches($line, '!?\[[^\]]*\]\(([^)\s]+)\)')) {
            $links.Add($match.Groups[1].Value)
        }
    }
    return $links
}

function Test-External([string]$target) {
    return $target -match '^(https?:|mailto:|#)' -or $target.StartsWith('../../')
}

# Links whose text is translated along with the heading cannot be compared between the two
# READMEs: an in-page anchor is '#tools' in English and '#инструменты' in Russian, and the
# language switch points at each other. Structural parity is enforced by the heading comparison
# above; here only the "same set of files is referenced" part matters.
function Get-ParallelLink([string]$target) {
    if ($target -match '^#') { return '<in-page anchor>' }
    if ($target -eq 'README.md' -or $target -eq 'README.ru.md') { return '<other-language README>' }
    return $target
}

# --- 1. The two READMEs must stay structurally parallel -------------------------------------
$en = Read-Lines $readmeEn
$ru = Read-Lines $readmeRu
$enHeadings = Get-Headings $en
$ruHeadings = Get-Headings $ru

if ($enHeadings.Count -ne $ruHeadings.Count) {
    $problems.Add("README.md has $($enHeadings.Count) headings, README.ru.md has $($ruHeadings.Count).")
}
else {
    for ($i = 0; $i -lt $enHeadings.Count; $i++) {
        if ($enHeadings[$i].Level -ne $ruHeadings[$i].Level) {
            $problems.Add("Heading $($i + 1) differs in level: '$($enHeadings[$i].Text)' vs '$($ruHeadings[$i].Text)'.")
        }
    }
}

$enLinks = @(Get-Links $en | ForEach-Object { Get-ParallelLink $_ })
$ruLinks = @(Get-Links $ru | ForEach-Object { Get-ParallelLink $_ })
$missingInRu = $enLinks | Where-Object { $ruLinks -notcontains $_ }
$missingInEn = $ruLinks | Where-Object { $enLinks -notcontains $_ }
foreach ($link in ($missingInRu | Select-Object -Unique)) { $problems.Add("Link only in README.md: $link") }
foreach ($link in ($missingInEn | Select-Object -Unique)) { $problems.Add("Link only in README.ru.md: $link") }

# --- 2. Every relative link and anchor must resolve ----------------------------------------
$markdownFiles = @(Get-ChildItem -Path $repoRoot -Recurse -File -Filter *.md |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|dist|\.git|\.mimocode)[\\/]' })

foreach ($file in $markdownFiles) {
    $relativeDir = Split-Path -Parent $file.FullName
    $isReadme = $file.Name -eq 'README.md' -or $file.Name -eq 'README.ru.md'
    $lines = Read-Lines $file.FullName
    $headings = Get-Headings $lines
    $anchors = @($headings | ForEach-Object { ConvertTo-Anchor $_.Text })

    foreach ($raw in (Get-Links $lines)) {
        $target = $raw
        if (Test-External $target) {
            if ($target -match '^#(.+)$') {
                if ($anchors -notcontains $Matches[1]) {
                    $problems.Add("$($file.Name): anchor '$raw' has no matching heading.")
                }
            }
            continue
        }

        $path = $target
        $anchor = $null
        if ($target.Contains('#')) {
            $path = $target.Substring(0, $target.IndexOf('#'))
            $anchor = $target.Substring($target.IndexOf('#') + 1)
        }

        if (-not $path) { continue }
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $relativeDir $path))
        if (-not (Test-Path $resolved)) {
            if (-not $isReadme -or $true) { $problems.Add("$($file.Name): '$target' points at a missing file.") }
            continue
        }

        if ($anchor -and $resolved.EndsWith('.md')) {
            $targetLines = [System.IO.File]::ReadAllLines($resolved, $utf8)
            $targetAnchors = @((Get-Headings $targetLines) | ForEach-Object { ConvertTo-Anchor $_.Text })
            if ($targetAnchors -notcontains $anchor) {
                $problems.Add("$($file.Name): '$target' - no heading in $(Split-Path -Leaf $resolved) produces '$anchor'.")
            }
        }
    }
}

# --- 3. No hard-coded tool count in prose --------------------------------------------------
# `tools/list` and `server_status` are the source of truth; a number in the text is a lie that
# nobody updates. The one number allowed is the [~] count, and it must match docs/tools.md.
$toolCountPatterns = @(
    '(?<!\d)(?<!\d )\b\d{2,3}\s+tools\b',
    '\b\d{2,3}\s+инструмент'
)

$toolboxLines = Read-Lines $toolbox
$notReachable = @($toolboxLines | Where-Object { $_ -match '^- `\[~\]` \*\*' }).Count

foreach ($pair in @(@{ Name = 'README.md'; Lines = $en }, @{ Name = 'README.ru.md'; Lines = $ru })) {
    for ($i = 0; $i -lt $pair.Lines.Count; $i++) {
        $line = $pair.Lines[$i]
        foreach ($pattern in $toolCountPatterns) {
            foreach ($match in [regex]::Matches($line, $pattern)) {
                $number = [int]([regex]::Match($match.Value, '\d+').Value)
                if ($number -eq $notReachable) { continue }
                $problems.Add("$($pair.Name):$($i + 1) hard-codes a tool count: '$($match.Value)'.")
            }
        }
    }
}

# The [~] count itself must be the real one.
$claims = @()
foreach ($pair in @(@{ Name = 'README.md'; Lines = $en }, @{ Name = 'README.ru.md'; Lines = $ru })) {
    for ($i = 0; $i -lt $pair.Lines.Count; $i++) {
        foreach ($match in [regex]::Matches($pair.Lines[$i], '(?<!\d)(\d{1,3})\s+(?:toolbox items|(?:пункта|инструмента) тулбокса)')) {
            $claims += [pscustomobject]@{ Where = "$($pair.Name):$($i + 1)"; Number = [int]$match.Groups[1].Value }
        }
    }
}
foreach ($claim in $claims) {
    if ($claim.Number -ne $notReachable) {
        $problems.Add("$($claim.Where) says $($claim.Number) toolbox items are unreachable, docs/tools.md has $notReachable.")
    }
}

# --- 4. The log location must be described as primary plus fallback ------------------------
foreach ($pair in @(@{ Name = 'README.md'; Lines = $en }, @{ Name = 'README.ru.md'; Lines = $ru })) {
    for ($i = 0; $i -lt $pair.Lines.Count; $i++) {
        if ($pair.Lines[$i] -match '%LOCALAPPDATA%\\CorelMcp\\corelmcp\.log') {
            if ($pair.Lines[$i] -notmatch 'fallback|запасн') {
                $problems.Add("$($pair.Name):$($i + 1) presents the LOCALAPPDATA log as the primary location; it is the fallback.")
            }
        }
    }
}

# --- Report --------------------------------------------------------------------------------
if ($problems.Count -eq 0) {
    Write-Host "docs check: OK ($($markdownFiles.Count) files, $($enHeadings.Count) headings paired)" -ForegroundColor Green
    exit 0
}

Write-Host "docs check: $($problems.Count) problem(s)" -ForegroundColor Red
foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Yellow }
exit 1
