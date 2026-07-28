[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('staging', 'production')]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$ApiImage,

    [Parameter(Mandatory)]
    [string]$WorkerImage,

    [Parameter(Mandatory)]
    [string]$WebImage,

    [Parameter(Mandatory)]
    [string]$PublicBaseUrl,

    [Parameter(Mandatory)]
    [string]$ReleaseRevision,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$imagePattern = '^[a-z0-9]+(?:[._-][a-z0-9]+)*(?::[0-9]+)?/[a-z0-9]+(?:[._/-][a-z0-9]+)*@sha256:[a-f0-9]{64}$'

function Assert-ImageReference {
    param(
        [Parameter(Mandatory)]
        [string]$Value,
        [Parameter(Mandatory)]
        [string]$Component
    )

    if ($Value -notmatch $imagePattern) {
        throw "$Component image must be a lowercase registry reference pinned by sha256 digest."
    }
}

function Invoke-Kustomize {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $renderedLines = & kubectl kustomize $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Kustomize rendering failed for $Path."
    }

    return ($renderedLines -join "`n") + "`n"
}

function Set-ReleaseValues {
    param(
        [Parameter(Mandatory)]
        [string]$Content,
        [Parameter(Mandatory)]
        [hashtable]$Images,
        [Parameter(Mandatory)]
        [uri]$PublicUri,
        [Parameter(Mandatory)]
        [string]$PlaceholderBaseUrl
    )

    $updated = $Content.Replace(
        'ghcr.io/yashraj-rathore/leadrecovery-api:sha-replace-me',
        $Images.Api)
    $updated = $updated.Replace(
        'ghcr.io/yashraj-rathore/leadrecovery-worker:sha-replace-me',
        $Images.Worker)
    $updated = $updated.Replace(
        'ghcr.io/yashraj-rathore/leadrecovery-web:sha-replace-me',
        $Images.Web)
    $updated = $updated.Replace($PlaceholderBaseUrl, $PublicUri.AbsoluteUri.TrimEnd('/'))
    $updated = $updated.Replace(([uri]$PlaceholderBaseUrl).Host, $PublicUri.Host)

    if ($updated.Contains('sha-replace-me')) {
        throw 'Rendered release manifests still contain a mutable image placeholder.'
    }

    return $updated
}

Assert-ImageReference -Value $ApiImage -Component 'API'
Assert-ImageReference -Value $WorkerImage -Component 'Worker'
Assert-ImageReference -Value $WebImage -Component 'Web'

if ($ReleaseRevision -notmatch '^[a-f0-9]{40}$') {
    throw 'ReleaseRevision must be a full lowercase 40-character Git commit SHA.'
}

$publicUri = $null
if (-not [uri]::TryCreate($PublicBaseUrl, [UriKind]::Absolute, [ref]$publicUri) -or
    $publicUri.Scheme -ne 'https' -or
    -not $publicUri.IsDefaultPort -or
    $publicUri.AbsolutePath -ne '/' -or
    -not [string]::IsNullOrEmpty($publicUri.Query) -or
    -not [string]::IsNullOrEmpty($publicUri.Fragment)) {
    throw 'PublicBaseUrl must be a root HTTPS origin using the default port.'
}

$placeholderBaseUrl = if ($Environment -eq 'staging') {
    'https://staging.leadrecovery.example.com'
}
else {
    'https://app.leadrecovery.example.com'
}

$images = @{
    Api = $ApiImage
    Worker = $WorkerImage
    Web = $WebImage
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

Push-Location $repositoryRoot
try {
    $migration = Invoke-Kustomize "deploy/kubernetes/$Environment/migration"
    $migration = Set-ReleaseValues `
        -Content $migration `
        -Images $images `
        -PublicUri $publicUri `
        -PlaceholderBaseUrl $placeholderBaseUrl

    $workloads = Invoke-Kustomize "deploy/kubernetes/$Environment"
    $workloads = Set-ReleaseValues `
        -Content $workloads `
        -Images $images `
        -PublicUri $publicUri `
        -PlaceholderBaseUrl $placeholderBaseUrl

    if ([regex]::Matches($migration, '(?m)^\s*image:\s+.+@sha256:[a-f0-9]{64}$').Count -ne 1) {
        throw 'Migration output must contain exactly one digest-pinned image.'
    }

    if ([regex]::Matches($workloads, '(?m)^\s*image:\s+.+@sha256:[a-f0-9]{64}$').Count -ne 3) {
        throw 'Workload output must contain exactly three digest-pinned images.'
    }

    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        (Join-Path $resolvedOutput 'migration.yaml'),
        $migration,
        $utf8WithoutBom)
    [System.IO.File]::WriteAllText(
        (Join-Path $resolvedOutput 'workloads.yaml'),
        $workloads,
        $utf8WithoutBom)

    $metadata = [ordered]@{
        environment = $Environment
        releaseRevision = $ReleaseRevision
        publicBaseUrl = $publicUri.AbsoluteUri.TrimEnd('/')
        images = [ordered]@{
            api = $ApiImage
            worker = $WorkerImage
            web = $WebImage
        }
    }
    $metadataJson = ($metadata | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $resolvedOutput 'release-metadata.json'),
        $metadataJson,
        $utf8WithoutBom)

    Write-Output "Rendered immutable $Environment release manifests in $resolvedOutput."
}
finally {
    Pop-Location
}
