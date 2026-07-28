[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Assert-Contains {
    param(
        [Parameter(Mandatory)]
        [string]$Content,
        [Parameter(Mandatory)]
        [string]$Pattern,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if ($Content -notmatch $Pattern) {
        throw $Message
    }
}

Push-Location $repositoryRoot
try {
    $dockerfiles = @(
        'deploy/docker/api.Dockerfile'
        'deploy/docker/worker.Dockerfile'
        'deploy/docker/web.Dockerfile'
    )
    foreach ($dockerfile in $dockerfiles) {
        $content = Get-Content -LiteralPath $dockerfile -Raw
        if ([regex]::Matches($content, '(?m)^FROM ').Count -lt 2) {
            throw "$dockerfile must use a multi-stage build."
        }

        Assert-Contains $content '(?m)^USER (app|node)$' `
            "$dockerfile must declare a non-root runtime user."
        Assert-Contains $content '(?m)^HEALTHCHECK ' `
            "$dockerfile must declare an OCI health check."
        Assert-Contains $content 'org\.opencontainers\.image\.version' `
            "$dockerfile must include OCI version metadata."
        Assert-Contains $content '@sha256:[a-f0-9]{64}' `
            "$dockerfile must pin its base image by digest."
    }

    $webDockerfile = Get-Content -LiteralPath 'deploy/docker/web.Dockerfile' -Raw
    Assert-Contains $webDockerfile `
        '(?m)^FROM node:24\.18\.0-alpine3\.23@sha256:595398b0081eacda8e1c4c5b97b76cd1020e4d58a8ebcb4843b9bca1e79e7436 AS runtime$' `
        'The web runtime must use the approved immutable Alpine base.'
    foreach ($unusedToolingPath in @(
            '/opt/yarn-v\*'
            '/usr/local/lib/node_modules/corepack'
            '/usr/local/lib/node_modules/npm'
        )) {
        Assert-Contains $webDockerfile $unusedToolingPath `
            "The web runtime must remove unused package tooling matching $unusedToolingPath."
    }

    $previousPassword = $env:POSTGRES_PASSWORD
    try {
        $env:POSTGRES_PASSWORD = 'deployment-artifact-validation-only'
        & docker compose config --quiet
        if ($LASTEXITCODE -ne 0) {
            throw 'docker compose config validation failed.'
        }
    }
    finally {
        $env:POSTGRES_PASSWORD = $previousPassword
    }

    $workloadOverlays = @('base', 'local', 'staging', 'production')
    foreach ($overlay in $workloadOverlays) {
        $path = "deploy/kubernetes/$overlay"
        $rendered = (& kubectl kustomize $path) -join "`n"
        if ($LASTEXITCODE -ne 0) {
            throw "Kustomize rendering failed for $path."
        }

        if ([regex]::Matches($rendered, '(?m)^kind: Deployment$').Count -ne 3) {
            throw "$path must render exactly three Deployments."
        }

        if ([regex]::Matches($rendered, '(?m)^kind: Service$').Count -ne 3) {
            throw "$path must render exactly three Services."
        }

        Assert-Contains $rendered '(?m)^kind: Ingress$' `
            "$path must render an Ingress."
        Assert-Contains $rendered 'secretKeyRef:' `
            "$path must use Secret references."
        Assert-Contains $rendered 'livenessProbe:' `
            "$path must define liveness probes."
        Assert-Contains $rendered 'readinessProbe:' `
            "$path must define readiness probes."
        Assert-Contains $rendered 'readOnlyRootFilesystem: true' `
            "$path must enforce read-only runtime filesystems."
        if ($rendered -match '(?m)^kind: Job$') {
            throw "$path must not start migrations concurrently with workloads."
        }
    }

    $migrationOverlays = @(
        'migration'
        'local/migration'
        'staging/migration'
        'production/migration'
    )
    foreach ($overlay in $migrationOverlays) {
        $path = "deploy/kubernetes/$overlay"
        $rendered = (& kubectl kustomize $path) -join "`n"
        if ($LASTEXITCODE -ne 0) {
            throw "Kustomize rendering failed for $path."
        }

        if ([regex]::Matches($rendered, '(?m)^kind: Job$').Count -ne 1) {
            throw "$path must render exactly one migration Job."
        }

        Assert-Contains $rendered '(?m)^\s*- --migrate$' `
            "$path must run the API migration-only mode."
        if ($rendered -match '(?m)^kind: Deployment$') {
            throw "$path must not render application Deployments."
        }
    }

    $production = (& kubectl kustomize deploy/kubernetes/production) -join "`n"
    Assert-Contains $production '(?m)^kind: HorizontalPodAutoscaler$' `
        'Production must render an API HPA.'
    if ([regex]::Matches($production, '(?m)^kind: PodDisruptionBudget$').Count -ne 2) {
        throw 'Production must render API and web PodDisruptionBudgets.'
    }
    Assert-Contains $production '(?m)^\s*- ReadWriteMany$' `
        'Production must use shared RWX data-protection storage.'

    $committedSecrets = Get-ChildItem -LiteralPath deploy -File -Recurse |
        Where-Object { $_.Extension -in @('.yaml', '.yml') } |
        Select-String -Pattern '^kind:\s*Secret\s*$'
    if ($committedSecrets) {
        throw 'Deployment YAML must not commit Kubernetes Secret objects.'
    }

    Write-Output 'Deployment artifact validation passed.'
}
finally {
    Pop-Location
}
