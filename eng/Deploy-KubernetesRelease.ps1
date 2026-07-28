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
    [ValidateSet('Deploy', 'Rollback')]
    [string]$Operation,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$namespace = "leadrecovery-$Environment"
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

function Invoke-Kubectl {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & kubectl @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "kubectl $($Arguments -join ' ') failed."
    }
}

function Invoke-PublicSmoke {
    param(
        [Parameter(Mandatory)]
        [string]$BaseUrl
    )

    foreach ($path in @('/', '/api/v1/auth/csrf')) {
        $uri = "$($BaseUrl.TrimEnd('/'))$path"
        $lastError = $null
        for ($attempt = 1; $attempt -le 12; $attempt++) {
            try {
                $response = Invoke-WebRequest `
                    -Uri $uri `
                    -Method Get `
                    -UseBasicParsing `
                    -TimeoutSec 15
                if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                    $lastError = $null
                    break
                }

                $lastError = "HTTP $($response.StatusCode)"
            }
            catch {
                $lastError = $_.Exception.Message
            }

            if ($attempt -lt 12) {
                Start-Sleep -Seconds 5
            }
        }

        if ($null -ne $lastError) {
            throw "Smoke check failed for $uri after 12 attempts: $lastError"
        }
    }
}

& (Join-Path $PSScriptRoot 'Render-KubernetesRelease.ps1') `
    -Environment $Environment `
    -ApiImage $ApiImage `
    -WorkerImage $WorkerImage `
    -WebImage $WebImage `
    -PublicBaseUrl $PublicBaseUrl `
    -ReleaseRevision $ReleaseRevision `
    -OutputDirectory $resolvedOutput

Invoke-Kubectl -Arguments @('get', 'namespace', $namespace)
Invoke-Kubectl -Arguments @('-n', $namespace, 'get', 'secret', 'leadrecovery-secrets')
Invoke-Kubectl -Arguments @('-n', $namespace, 'get', 'secret', 'leadrecovery-tls')

$previousImages = @()
$deploymentJson = & kubectl -n $namespace get deployments -o json
if ($LASTEXITCODE -eq 0) {
    $deployments = ($deploymentJson -join "`n") | ConvertFrom-Json
    foreach ($deployment in $deployments.items) {
        foreach ($container in $deployment.spec.template.spec.containers) {
            $previousImages += [ordered]@{
                deployment = $deployment.metadata.name
                container = $container.name
                image = $container.image
            }
        }
    }
}

if ($Operation -eq 'Deploy') {
    Invoke-Kubectl -Arguments @(
        '-n', $namespace,
        'delete', 'job', 'leadrecovery-migrate',
        '--ignore-not-found=true',
        '--wait=true')
    Invoke-Kubectl -Arguments @('apply', '-f', (Join-Path $resolvedOutput 'migration.yaml'))

    & kubectl -n $namespace wait `
        --for=condition=complete `
        job/leadrecovery-migrate `
        --timeout=10m
    if ($LASTEXITCODE -ne 0) {
        & kubectl -n $namespace logs job/leadrecovery-migrate
        throw 'Database migration did not complete; workloads were not changed.'
    }

    Invoke-Kubectl -Arguments @('-n', $namespace, 'logs', 'job/leadrecovery-migrate')
}

Invoke-Kubectl -Arguments @('apply', '-f', (Join-Path $resolvedOutput 'workloads.yaml'))

foreach ($deployment in @('leadrecovery-api', 'leadrecovery-worker', 'leadrecovery-web')) {
    Invoke-Kubectl -Arguments @(
        '-n', $namespace,
        'rollout', 'status',
        "deployment/$deployment",
        '--timeout=5m')
}

$expectedImages = @{
    'leadrecovery-api' = $ApiImage
    'leadrecovery-worker' = $WorkerImage
    'leadrecovery-web' = $WebImage
}
foreach ($deployment in $expectedImages.Keys) {
    $deployedImage = (& kubectl -n $namespace get "deployment/$deployment" `
        -o 'jsonpath={.spec.template.spec.containers[0].image}') -join ''
    if ($LASTEXITCODE -ne 0 -or $deployedImage -ne $expectedImages[$deployment]) {
        throw "$deployment did not retain the requested immutable image."
    }
}

Invoke-PublicSmoke -BaseUrl $PublicBaseUrl

$recordPath = Join-Path $resolvedOutput 'deployment-record.json'
$record = [ordered]@{
    operation = $Operation
    environment = $Environment
    releaseRevision = $ReleaseRevision
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    images = [ordered]@{
        api = $ApiImage
        worker = $WorkerImage
        web = $WebImage
    }
    previousImages = $previousImages
    migrationsApplied = $Operation -eq 'Deploy'
    smokePaths = @('/', '/api/v1/auth/csrf')
}
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$recordJson = ($record | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"
[System.IO.File]::WriteAllText($recordPath, $recordJson, $utf8WithoutBom)

Write-Output "$Operation completed for $Environment at revision $ReleaseRevision."
