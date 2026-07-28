# ADR-0024: Immutable CI/CD promotion and rollback

- Status: Accepted
- Date: 2026-07-28

## Context

LR-0903 must turn the LR-0901/LR-0902 container and Kubernetes baseline into a
release path with pull-request gates, authenticated image scanning, staging
verification, explicit production approval, and a tested rollback. The path
must not commit registry, cluster, database, TLS, or provider credentials. A
failed release must not silently reverse a PostgreSQL migration because a
destructive schema rollback can make recovery worse.

GitHub-hosted workflows also execute third-party automation. The Trivy project
reported a 2026 action-tag compromise, so mutable action references are not an
acceptable supply-chain boundary even when the action's release tag is called
immutable.

## Decision

1. Run backend formatting/build/analyzers/tests, frontend type-check/build/E2E,
   OpenAPI validation, dependency audits, repository secret scanning, deployment
   policy tests, and three no-push image builds/scans on pull requests.
2. Pin every external GitHub Action by its full commit SHA. Dependabot proposes
   reviewed updates for Actions, NuGet, pnpm, Dockerfiles, and Compose.
3. Use Redocly CLI 2.40.0 for OpenAPI validation and the SHA-pinned Trivy action
   v0.36.0 with Trivy v0.70.0 for secret and High/Critical image gates. The
   scanner job never receives Kubernetes environment secrets; release scans
   receive read-only package access.
4. Start a release only from a `vMAJOR.MINOR.PATCH` tag whose commit is reachable
   from `main`. Publish API, worker, and web images to GHCR with version and full
   commit-SHA tags, OCI provenance, and SBOM attestations.
5. Treat the registry digest returned by BuildKit as the release identity.
   Staging and production render and deploy the exact same three digest
   references; neither environment deploys a tag.
6. Keep `KUBE_CONFIG_B64` in each GitHub environment's secrets and the root HTTPS
   `PUBLIC_BASE_URL` in its variables. The workflow contains only references.
   Namespace, application/TLS secrets, database recovery points, and other
   environment prerequisites remain externally managed.
7. Apply and wait for the one-shot migration Job before changing workloads.
   Rollout completion, exact deployed-image verification, and public web/API
   smoke checks gate promotion.
8. Stop the Release workflow after successful staging. Production requires a
   separate manual `Promote Production` dispatch with the successful Release run
   ID plus explicit staging-smoke and database-recovery-point confirmations. The
   workflow verifies the run, downloads its staging record, and derives image
   digests from that evidence instead of accepting operator-supplied images.
   Promotion and rollback jobs refuse dispatches from refs other than `main`.
9. Also use the GitHub `production` environment as an independent approval
   boundary. Repository administrators must configure required reviewers and
   prevent self-review; workflow YAML cannot create or enforce those repository
   rules. The separate manual dispatch remains fail-closed when reviewers have
   not yet been configured.
10. Record rendered manifests, current/previous image coordinates, revision, and
   smoke result as release artifacts. Do not put Secret objects or values in the
   record.
11. Implement rollback as a separate manual workflow using current trusted
    deployment tooling from `main`. It accepts only a prior commit reachable from
    `main` and three SHA-256 image digests, reuses the selected protected
    environment, and requires an explicit database-compatibility confirmation.
12. Rollback changes application images only. It never runs or reverses a
    migration. Incompatible schema changes require the rehearsed database restore
    or a forward fix before the prior application can be selected.

## Consequences

A release artifact can be promoted and restored without rebuilding or resolving
a mutable tag. A staging failure blocks production, and production cannot start
without a separate manual dispatch tied to that successful run. A configured
production reviewer sees the exact release after it has passed the authenticated
image gate and staging smoke test. Registry publication precedes the scan, but
an unscanned or failed digest cannot reach either deployment job.

The repository owner must still configure branch protection, GitHub environments,
reviewers, cluster access, URLs, external Secrets, ingress/TLS, storage, and a
database recovery point. The first hosted release will fail closed until those
prerequisites exist. Application rollback is deliberately unavailable when
database compatibility has not been confirmed.
