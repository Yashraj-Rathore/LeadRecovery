[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$targetPath = Join-Path $repositoryRoot 'CODEX_MASTER_IMPLEMENTATION_SPEC.md'
$sourcePaths = @(
    'README.md'
    'AGENTS.md'
    'CODEX_START_HERE.md'
    'docs/01_PRODUCT_REQUIREMENTS.md'
    'docs/02_SYSTEM_ARCHITECTURE.md'
    'docs/03_DOMAIN_AND_DATABASE.md'
    'docs/04_API_AND_INTEGRATIONS.md'
    'docs/05_FRONTEND_UX.md'
    'docs/06_AI_GUARDRAILS.md'
    'docs/07_SECURITY_PRIVACY.md'
    'docs/08_TESTING_QUALITY.md'
    'docs/09_DEVOPS_KUBERNETES.md'
    'docs/10_OBSERVABILITY_OPERATIONS.md'
    'docs/11_TIMELINE_AND_MILESTONES.md'
    'docs/12_BACKLOG_AND_ACCEPTANCE.md'
    'docs/13_PILOT_AND_VALIDATION.md'
    'docs/14_SAAS_EVOLUTION.md'
    'docs/decisions/README.md'
    'docs/decisions/0001-modular-monolith-and-project-boundaries.md'
    'docs/decisions/0002-pinned-technology-baseline.md'
    'docs/decisions/0003-tenant-isolation.md'
    'docs/decisions/0004-transactional-background-work.md'
    'docs/decisions/0005-api-contract-and-concurrency.md'
    'docs/decisions/0006-lead-lifecycle-and-webhook-identity.md'
    'docs/decisions/0007-tenant-context-and-concurrency.md'
    'docs/decisions/0008-customer-phone-normalization.md'
    'docs/decisions/0009-conversation-and-message-lifecycle.md'
    'docs/decisions/0010-scheduled-actions-and-external-receipts.md'
    'docs/decisions/0011-identity-membership-and-browser-session.md'
    'docs/decisions/0012-twilio-call-status-ingestion.md'
    'docs/decisions/0013-sms-worker-and-webhook-lifecycle.md'
    'docs/decisions/0014-operational-dashboard-and-manual-sms.md'
    'docs/decisions/0015-deterministic-qualification-booking-and-follow-up.md'
    'docs/decisions/0016-structured-lead-analysis-adapter.md'
    'docs/decisions/0017-human-reviewed-ai-analysis.md'
    'docs/decisions/0018-ai-workflow-invocation-and-fallback.md'
    'CODEX_PROMPT_SEQUENCE.md'
    'templates/definition-of-done.md'
)

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('# LeadRecovery - Complete Codex Implementation Specification')
[void]$builder.AppendLine('> Generated from the modular repository documentation by eng/Sync-MasterSpecification.ps1. Edit the source files, not this file.')
[void]$builder.AppendLine()
[void]$builder.AppendLine('---')

foreach ($sourcePath in $sourcePaths) {
    $absolutePath = Join-Path $repositoryRoot $sourcePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Required specification source '$sourcePath' does not exist."
    }

    $content = [System.IO.File]::ReadAllText($absolutePath).TrimEnd([char[]]"`r`n")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("<!-- SOURCE: $sourcePath -->")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine($content)
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('---')
}

$normalizedContent = $builder.ToString().Replace("`r`n", "`n")
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($targetPath, $normalizedContent, $utf8WithoutBom)

Write-Output "Regenerated $targetPath from $($sourcePaths.Count) source files."
