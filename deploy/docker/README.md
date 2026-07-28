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
| Node.js | 24.18.0 Bookworm slim | `sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d` |

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
- Compose defaults to the fake SMS provider, disabled AI, disabled retention,
  and disabled global automation. Real provider activity requires explicit
  operator configuration.
- Build arguments populate OCI version, revision, and creation labels. Release
  automation must use an immutable image tag and commit SHA.

## Vulnerability gate and recorded exception

For a release, scan all runtime images and fail on fixable High or Critical
findings, for example with an authenticated Docker Scout installation:

```powershell
docker scout cves --only-severity critical,high --only-fixed --exit-code leadrecovery-api:<immutable-tag>
docker scout cves --only-severity critical,high --only-fixed --exit-code leadrecovery-worker:<immutable-tag>
docker scout cves --only-severity critical,high --only-fixed --exit-code leadrecovery-web:<immutable-tag>
```

LR-0901 validation on 2026-07-28 could not execute Docker Scout because the
installed Scout 1.0.9 client required a separate Docker account login. Running
an unapproved third-party scanner against private built-image contents was not
permitted. This is the documented scan exception: the images use current,
verified vendor digests and the application dependency audits pass, but the
images are not approved for a production release until the authenticated image
gate is completed. LR-0903 owns adding that authenticated CI gate and retaining
its report/SBOM.

Run `eng/Test-DeploymentArtifacts.ps1` to revalidate the Dockerfile, Compose,
Kustomize, migration-order, security-context, and no-committed-Secret
invariants before submitting a deployment change.
