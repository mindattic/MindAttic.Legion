#requires -Version 5.1
<#
  SessionStart hook for MindAttic.Legion (Codex).
  Reads docs/BIBLE.digest.md and emits Claude Code SessionStart hook JSON that
  injects the digest as authoritative context. If the digest is missing or empty,
  emits {} (no-op). Non-ASCII is escaped to \uXXXX so output is safe under
  Windows PowerShell 5.1 / Win-1252 consoles.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here     = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $here)
$digest   = Join-Path $repoRoot 'docs/BIBLE.digest.md'

function Write-Empty { Write-Output '{}'; exit 0 }

if (-not (Test-Path -LiteralPath $digest)) { Write-Empty }
$body = Get-Content -LiteralPath $digest -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($body)) { Write-Empty }

$preamble = @'
[CODEX - AUTHORITATIVE PROJECT CONTEXT for MindAttic.Legion]
The following digest is the source of truth for what this project IS, is NOT, and
the Laws that keep it coherent. It is generated from docs/BIBLE.md. When in doubt,
defer to it and to docs/BIBLE.md (full detail). House rules are inherited from
MindAttic.HouseRules.md. Do not contradict the Laws below.

'@

$context = $preamble + $body

# JSON-encode the additionalContext string, escaping all non-ASCII to \uXXXX.
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $context.ToCharArray()) {
    $code = [int][char]$ch
    switch ($ch) {
        '"'  { [void]$sb.Append('\"') }
        '\'  { [void]$sb.Append('\\') }
        "`b" { [void]$sb.Append('\b') }
        "`f" { [void]$sb.Append('\f') }
        "`n" { [void]$sb.Append('\n') }
        "`r" { [void]$sb.Append('\r') }
        "`t" { [void]$sb.Append('\t') }
        default {
            if ($code -lt 32 -or $code -gt 126) {
                [void]$sb.Append('\u' + $code.ToString('x4'))
            } else {
                [void]$sb.Append($ch)
            }
        }
    }
}

$json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $sb.ToString() + '"}}'
Write-Output $json
