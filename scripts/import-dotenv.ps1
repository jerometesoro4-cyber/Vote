<#
.SYNOPSIS
    Imports KEY=VALUE entries from a .env file into the current PowerShell process environment.

.DESCRIPTION
    ASP.NET Core does not load .env files automatically. Dot-source this helper before running
    database migrations or starting the app:
        . .\scripts\import-dotenv.ps1

    Values are parsed literally without shell expansion, preserving spaces, '=', '#', '\', and '$'.
    Existing process environment variables take precedence unless -Overwrite is passed.
    With -Overwrite, empty values clear any previously set variable of that name in the process.
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) ".env"),
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"

$resolvedPath = if ([System.IO.Path]::IsPathRooted($Path)) {
    $Path
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

if (-not [System.IO.File]::Exists($resolvedPath)) {
    throw "Environment file not found: '$resolvedPath'. Copy .env.example to .env first (Copy-Item .env.example .env)."
}

$lines = [System.IO.File]::ReadAllLines($resolvedPath, [System.Text.Encoding]::UTF8)

foreach ($rawLine in $lines) {
    if ([string]::IsNullOrWhiteSpace($rawLine)) {
        continue
    }

    $trimmed = $rawLine.Trim()
    if ($trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
        continue
    }

    $working = $trimmed
    if ($working.StartsWith("export ", [System.StringComparison]::Ordinal)) {
        $working = $working.Substring(7).TrimStart()
    }

    $eqIndex = $working.IndexOf("=")
    if ($eqIndex -le 0) {
        continue
    }

    $key = $working.Substring(0, $eqIndex).Trim()
    if ($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        continue
    }

    $rawValue = $working.Substring($eqIndex + 1).Trim()
    if ($rawValue.Length -ge 2) {
        $firstChar = $rawValue[0]
        $lastChar = $rawValue[$rawValue.Length - 1]
        if (($firstChar -eq '"' -and $lastChar -eq '"') -or ($firstChar -eq "'" -and $lastChar -eq "'")) {
            $value = $rawValue.Substring(1, $rawValue.Length - 2)
        } else {
            $value = $rawValue
        }
    } else {
        $value = $rawValue
    }

    $existing = [System.Environment]::GetEnvironmentVariable($key, "Process")
    if ($null -ne $existing -and -not $Overwrite) {
        continue
    }

    if ($value.Length -eq 0) {
        if ($Overwrite) {
            [System.Environment]::SetEnvironmentVariable($key, $null, "Process")
        }
        continue
    }

    [System.Environment]::SetEnvironmentVariable($key, $value, "Process")
}
