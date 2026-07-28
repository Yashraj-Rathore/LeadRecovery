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

function Get-NormalizedHash {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $content = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

Push-Location $repositoryRoot
try {
    $ci = Get-Content -LiteralPath '.github/workflows/ci.yml' -Raw
    $release = Get-Content -LiteralPath '.github/workflows/release.yml' -Raw
    $promotion = Get-Content -LiteralPath '.github/workflows/promote-production.yml' -Raw
    $rollback = Get-Content -LiteralPath '.github/workflows/rollback.yml' -Raw
    $dependabot = Get-Content -LiteralPath '.github/dependabot.yml' -Raw

    Assert-Contains $ci '(?m)^  pull_request:$' 'CI must run on pull requests.'
    Assert-Contains $ci 'dotnet format LeadRecovery\.sln' 'CI must verify backend formatting.'
    Assert-Contains $ci 'dotnet build LeadRecovery\.sln.+--warnaserror' `
        'CI must compile with warnings as errors.'
    Assert-Contains $ci 'dotnet test LeadRecovery\.sln' 'CI must run backend tests.'
    Assert-Contains $ci 'pnpm frontend:typecheck' 'CI must type-check the frontend.'
    Assert-Contains $ci 'pnpm frontend:build' 'CI must build the frontend.'
    Assert-Contains $ci 'pnpm e2e' 'CI must run browser acceptance tests.'
    Assert-Contains $ci 'pnpm openapi:lint' 'CI must validate the OpenAPI contract.'
    Assert-Contains $ci 'dotnet list LeadRecovery\.sln package --vulnerable --include-transitive' `
        'CI must audit NuGet dependencies.'
    Assert-Contains $ci 'pnpm audit --audit-level high' 'CI must audit frontend dependencies.'
    Assert-Contains $ci '(?s)scanners:\s+secret.+exit-code:\s+"1"' `
        'CI must fail when the repository secret scan finds a leak.'
    Assert-Contains $ci '(?s)Build without pushing.+push:\s+false' `
        'PR CI must build containers without publishing them.'
    Assert-Contains $ci '(?s)Reject High or Critical image vulnerabilities.+severity:\s+HIGH,CRITICAL' `
        'PR CI must gate High and Critical image vulnerabilities.'
    Assert-Contains $ci 'Test-CiCdArtifacts\.ps1' `
        'CI must validate its deployment and release policies.'

    Assert-Contains $release '(?m)^      - "v\*\.\*\.\*"$' `
        'Release workflow must be tag-triggered.'
    Assert-Contains $release '(?m)^      packages: write$' `
        'Only the image publication job may request package write access.'
    Assert-Contains $release '(?s)tags:\s+\|.+:sha-\$\{\{ github\.sha \}\}' `
        'Release images must carry a commit SHA tag.'
    Assert-Contains $release '(?m)^          provenance: mode=max$' `
        'Release images must publish provenance.'
    Assert-Contains $release '(?m)^          sbom: true$' `
        'Release images must publish an SBOM attestation.'
    Assert-Contains $release '(?s)name: Scan published images.+severity:\s+HIGH,CRITICAL' `
        'Published image digests must pass a High/Critical scan.'
    Assert-Contains $release '(?s)environment:\s+name: staging' `
        'Release workflow must deploy and smoke-test staging.'
    if ($release -match '(?m)^  deploy-production:$') {
        throw 'Release workflow must stop after staging instead of auto-deploying production.'
    }
    Assert-Contains $release '\$\{\{ secrets\.KUBE_CONFIG_B64 \}\}' `
        'Cluster credentials must come from protected environment secrets.'
    Assert-Contains $release '\$\{\{ vars\.PUBLIC_BASE_URL \}\}' `
        'Public smoke URLs must come from environment configuration.'
    Assert-Contains $release "Operation = 'Deploy'" `
        'Release promotion must use the migration-first deployment operation.'

    Assert-Contains $promotion '(?m)^  workflow_dispatch:$' `
        'Production promotion must require an explicit manual dispatch.'
    Assert-Contains $promotion 'release_run_id' `
        'Production promotion must identify a completed release run.'
    Assert-Contains $promotion 'confirm_staging_smoke' `
        'Production promotion must require staging smoke confirmation.'
    Assert-Contains $promotion 'confirm_database_recovery_point' `
        'Production promotion must require recovery-point confirmation.'
    Assert-Contains $promotion '(?m)^      name: production$' `
        'Production promotion must use the production environment.'
    Assert-Contains $promotion "if: github.ref == 'refs/heads/main'" `
        'Production promotion must reject dispatches from non-main refs.'
    Assert-Contains $promotion '(?s)actions/runs/\$RELEASE_RUN_ID.+release\.yml.+conclusion.+success' `
        'Production promotion must validate a successful Release workflow run.'
    Assert-Contains $promotion 'actions/download-artifact@[a-f0-9]{40}' `
        'Production promotion must download the successful staging record.'
    Assert-Contains $promotion '(?s)release-metadata\.json.+deployment-record\.json' `
        'Production promotion must validate immutable staging metadata and smoke evidence.'
    Assert-Contains $promotion "Operation = 'Deploy'" `
        'Production promotion must remain migration-first.'

    Assert-Contains $rollback '(?m)^  workflow_dispatch:$' `
        'Rollback must require an explicit operator dispatch.'
    Assert-Contains $rollback 'confirm_database_compatibility' `
        'Rollback must require database compatibility confirmation.'
    Assert-Contains $rollback '(?m)^      name: \$\{\{ inputs\.target_environment \}\}$' `
        'Rollback must use the selected protected environment.'
    Assert-Contains $rollback "if: github.ref == 'refs/heads/main'" `
        'Rollback must reject dispatches from non-main refs.'
    Assert-Contains $rollback '(?s)Check out current trusted deployment tooling.+ref: main' `
        'Rollback must use current trusted deployment tooling from main.'
    Assert-Contains $rollback "Operation = 'Rollback'" `
        'Rollback must use the non-migrating deployment operation.'
    Assert-Contains $rollback '\^sha256:\[a-f0-9\]\{64\}\$' `
        'Rollback inputs must require immutable image digests.'

    foreach ($workflow in @($ci, $release, $promotion, $rollback)) {
        $externalUses = [regex]::Matches(
            $workflow,
            '(?m)^\s*uses:\s+(?!\./)(?<action>[^@\s]+)@(?<reference>[^\s#]+)')
        foreach ($use in $externalUses) {
            if ($use.Groups['reference'].Value -notmatch '^[a-f0-9]{40}$') {
                throw "External action $($use.Groups['action'].Value) must use a full commit SHA."
            }
        }
    }

    foreach ($ecosystem in @('nuget', 'npm', 'github-actions', 'docker')) {
        Assert-Contains $dependabot "package-ecosystem:\s+$ecosystem" `
            "Dependabot must cover $ecosystem dependencies."
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
        "leadrecovery-cicd-$([Guid]::NewGuid().ToString('N'))")
    $releaseA = Join-Path $temporaryRoot 'release-a'
    $releaseB = Join-Path $temporaryRoot 'release-b'
    $rollbackA = Join-Path $temporaryRoot 'rollback-a'
    $productionA = Join-Path $temporaryRoot 'production-a'
    $digestA = 'a' * 64
    $digestB = 'b' * 64
    $revisionA = '1' * 40
    $revisionB = '2' * 40

    try {
        & ./eng/Render-KubernetesRelease.ps1 `
            -Environment staging `
            -ApiImage "registry.example.test/leadrecovery-api@sha256:$digestA" `
            -WorkerImage "registry.example.test/leadrecovery-worker@sha256:$digestA" `
            -WebImage "registry.example.test/leadrecovery-web@sha256:$digestA" `
            -PublicBaseUrl 'https://staging.test.invalid' `
            -ReleaseRevision $revisionA `
            -OutputDirectory $releaseA

        & ./eng/Render-KubernetesRelease.ps1 `
            -Environment staging `
            -ApiImage "registry.example.test/leadrecovery-api@sha256:$digestB" `
            -WorkerImage "registry.example.test/leadrecovery-worker@sha256:$digestB" `
            -WebImage "registry.example.test/leadrecovery-web@sha256:$digestB" `
            -PublicBaseUrl 'https://staging.test.invalid' `
            -ReleaseRevision $revisionB `
            -OutputDirectory $releaseB

        & ./eng/Render-KubernetesRelease.ps1 `
            -Environment staging `
            -ApiImage "registry.example.test/leadrecovery-api@sha256:$digestA" `
            -WorkerImage "registry.example.test/leadrecovery-worker@sha256:$digestA" `
            -WebImage "registry.example.test/leadrecovery-web@sha256:$digestA" `
            -PublicBaseUrl 'https://staging.test.invalid' `
            -ReleaseRevision $revisionA `
            -OutputDirectory $rollbackA

        & ./eng/Render-KubernetesRelease.ps1 `
            -Environment production `
            -ApiImage "registry.example.test/leadrecovery-api@sha256:$digestA" `
            -WorkerImage "registry.example.test/leadrecovery-worker@sha256:$digestA" `
            -WebImage "registry.example.test/leadrecovery-web@sha256:$digestA" `
            -PublicBaseUrl 'https://production.test.invalid' `
            -ReleaseRevision $revisionA `
            -OutputDirectory $productionA

        $firstReleaseHash = Get-NormalizedHash (Join-Path $releaseA 'workloads.yaml')
        $secondReleaseHash = Get-NormalizedHash (Join-Path $releaseB 'workloads.yaml')
        $rollbackHash = Get-NormalizedHash (Join-Path $rollbackA 'workloads.yaml')
        if ($firstReleaseHash -eq $secondReleaseHash) {
            throw 'Different release digests must render different workload manifests.'
        }
        if ($firstReleaseHash -ne $rollbackHash) {
            throw 'Rendering prior digests must restore the exact prior workload manifest.'
        }

        $rollbackManifest = Get-Content -LiteralPath (
            Join-Path $rollbackA 'workloads.yaml') -Raw
        if ([regex]::Matches(
                $rollbackManifest,
                '(?m)^\s*image:\s+.+@sha256:[a-f0-9]{64}$').Count -ne 3) {
            throw 'Rollback manifest must contain three immutable application images.'
        }

        $productionManifest = Get-Content -LiteralPath (
            Join-Path $productionA 'workloads.yaml') -Raw
        Assert-Contains $productionManifest 'host: production\.test\.invalid' `
            'Production release rendering must replace the placeholder ingress host.'

        $mutableRejected = $false
        try {
            & ./eng/Render-KubernetesRelease.ps1 `
                -Environment staging `
                -ApiImage 'registry.example.test/leadrecovery-api:latest' `
                -WorkerImage "registry.example.test/leadrecovery-worker@sha256:$digestA" `
                -WebImage "registry.example.test/leadrecovery-web@sha256:$digestA" `
                -PublicBaseUrl 'https://staging.test.invalid' `
                -ReleaseRevision $revisionA `
                -OutputDirectory (Join-Path $temporaryRoot 'invalid')
        }
        catch {
            $mutableRejected = $true
        }
        if (-not $mutableRejected) {
            throw 'Release rendering must reject mutable image tags.'
        }
    }
    finally {
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporaryRoot = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::GetTempPath())
        if ($resolvedTemporaryRoot.StartsWith(
                $systemTemporaryRoot,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }

    Write-Output 'CI/CD artifact validation passed, including deterministic rollback rendering.'
}
finally {
    Pop-Location
}
