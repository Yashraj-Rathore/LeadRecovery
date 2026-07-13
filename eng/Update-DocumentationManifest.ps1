[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'MANIFEST.json'

$fixedPaths = @(
    'AGENTS.md'
    'CHANGELOG.md'
    'CODEX_MASTER_IMPLEMENTATION_SPEC.md'
    'CODEX_PROMPT_SEQUENCE.md'
    'CODEX_START_HERE.md'
    'README.md'
    'api/openapi.yaml'
    'database/schema.sql'
)

$discoveredPaths = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'diagrams') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'templates') -File -Recurse
) | ForEach-Object {
    $_.FullName.Substring($repositoryRoot.Length).TrimStart([char[]]'\/').Replace('\', '/')
}

$manifest = @($fixedPaths + $discoveredPaths) |
    Sort-Object -Unique |
    ForEach-Object {
        $absolutePath = Join-Path $repositoryRoot $_
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            throw "Documentation artifact '$_' does not exist."
        }

        [ordered]@{
            path = $_
            bytes = (Get-Item -LiteralPath $absolutePath).Length
        }
    }

$json = ($manifest | ConvertTo-Json -Depth 3).Replace("`r`n", "`n") + "`n"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($manifestPath, $json, $utf8WithoutBom)

Write-Output "Updated $manifestPath with $($manifest.Count) documentation artifacts."
