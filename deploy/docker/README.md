# Docker deployment

LR-0901 provides production images for the API, worker, and Next.js dashboard.
All three use multi-stage builds, immutable base-image digests, OCI version
metadata, non-root runtime users, and container health checks. The API image
also supplies the one-shot `--migrate` mode used by Compose and Kubernetes.

## Pinned image toolchain

| Stage | Version | Immutable digest |
|---|---:|---|
| .NET SDK | 10.0.302 | `sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664` |
| ASP.NET Core runtime | 10.0.10 | `sha256:1fa23fc4872d95fd71c2833ebe65d7e84a43b2d51a31d119516852f13d9505a7` |
| Node.js build | 24.18.0 Bookworm slim | `sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d` |
| Node.js runtime | 24.18.0 Alpine 3.23 | `sha256:595398b0081eacda8e1c4c5b97b76cd1020e4d58a8ebcb4843b9bca1e79e7436` |

The .NET container SDK may roll forward within the `10.0.3xx` feature band
selected by `global.json`. Application packages remain locked independently.
Review and update each tag and digest together; never update a digest without
rebuilding and rerunning the complete validation suite.

## Build and run locally

Copy the local template, replace its example database password, and attach
release metadata to the build:

```powershell
Copy-Item templates/.env.example .env
$env:IMAGE_VERSION = '0.9.0-local'
$env:IMAGE_REVISION = git rev-parse HEAD
$env:IMAGE_CREATED = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
docker compose build --pull
docker compose up --detach --no-build
docker compose ps
```

Compose waits for PostgreSQL, runs the migration container to completion, then
starts the API and worker. The web container starts only after the API is
healthy. Defaults expose web on `3000`, API on `8080`, and worker health on
`8081`; the `.env` values can override those host ports.

Verify the stack:

```powershell
Invoke-WebRequest http://localhost:8080/health/ready -UseBasicParsing
Invoke-WebRequest http://localhost:8081/health/ready -UseBasicParsing
Invoke-WebRequest http://localhost:3000/ -UseBasicParsing
docker compose logs migrate
```

Use `docker compose down` to stop the stack. Add `--volumes` only for a
disposable environment because it deletes local PostgreSQL and data-protection
state.

## Runtime safety

- API and worker run as the .NET image `app` user; web runs as `node`.
- Runtime images contain no compiler, SDK, or added OS package. The .NET OCI
  check uses Bash TCP support already present in the pinned runtime image.
- The web runtime contains Node.js but removes npm, Corepack, pnpm, and Yarn;
  dependency installation remains isolated to the discarded build stages.
- Compose defaults to the fake SMS provider, disabled AI, disabled retention,
  and disabled global automation. Real provider activity requires explicit
  operator configuration.
- Build arguments populate OCI version, revision, and creation labels. Release
  automation must use an immutable image tag and commit SHA.

## Vulnerability gate

LR-0903 closes the LR-0901 local Docker Scout exception with an authenticated
CI gate. Pull requests build all three images without publishing them and fail
on any detected High or Critical OS/library vulnerability. Release tags publish
the images to GHCR, then a separate package-read-only job scans the exact
registry digests before staging receives Kubernetes credentials. Production
cannot run automatically: the manual promotion workflow validates the selected
Release run and its successful staging deployment record before it receives
production credentials.

The release build also publishes BuildKit SBOM and provenance attestations.
Trivy action v0.36.0 and Trivy v0.70.0 are pinned; the action uses its full
verified commit SHA rather than a mutable tag. This addresses the upstream
2026 Trivy action-tag incident documented in
`https://github.com/aquasecurity/trivy/security/advisories/GHSA-69fq-xp46-6x23`.

Docker Scout remains an optional equivalent local check:

```powershell
docker scout cves --only-severity critical,high --exit-code leadrecovery-api:<immutable-tag>
docker scout cves --only-severity critical,high --exit-code leadrecovery-worker:<immutable-tag>
docker scout cves --only-severity critical,high --exit-code leadrecovery-web:<immutable-tag>
```

An image is not approved merely because it was pushed. Only the immutable
digest recorded by a successful `Scan published images` release job may be
promoted. Any exception must be documented in a new security decision; the
workflow contains no severity bypass or committed allowlist.

Run `eng/Test-DeploymentArtifacts.ps1` and `eng/Test-CiCdArtifacts.ps1` to
revalidate the Dockerfile, Compose, Kustomize, migration-order, rollback,
security-context, and no-committed-Secret invariants before submitting a
deployment change.
