[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "tests/LeadRecovery.IntegrationTests/LeadRecovery.IntegrationTests.csproj"
$proofs = @(
    "DuplicateCallbackHasNoDuplicateEffect",
    "SignedStopIsIdempotentCancelsPendingActionAndBlocksFutureSend"
)

Push-Location $repositoryRoot
try {
    foreach ($proof in $proofs) {
        Write-Host "Running demo proof: $proof"
        dotnet test $project --configuration $Configuration --no-build -- --filter-method "*$proof"
        if ($LASTEXITCODE -ne 0) {
            throw "Demo proof failed: $proof"
        }
    }

    Write-Host "Demo proofs passed: duplicate callbacks are idempotent; STOP cancels and blocks automation."
}
finally {
    Pop-Location
}
