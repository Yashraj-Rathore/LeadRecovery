# Kubernetes deployment

LR-0902 provides plain Kustomize manifests for the API, worker, and web
workloads. PostgreSQL remains an external managed dependency; the repository
does not deploy a database, credentials, or provider secrets into the cluster.

## Layout

```text
deploy/kubernetes/
|-- base/                 # shared workloads, services, ingress, policy
|   `-- foundation/       # namespace, ConfigMap, ServiceAccounts
|-- migration/            # reusable migration Job base
|-- local/                # local workload and migration overlays
|-- staging/              # staging workload and migration overlays
`-- production/           # HA workload and migration overlays
```

Migration overlays are deliberately separate from workload overlays. Apply and
wait for the Job before applying Deployments; API pods never migrate the schema
at startup.

## Environment prerequisites

- a Kubernetes cluster and `kubectl` with Kustomize support;
- an `nginx` IngressClass (or an environment patch selecting another class);
- externally reachable PostgreSQL 18 and a current backup/PITR recovery point;
- a TLS Secret named `leadrecovery-tls` outside local development;
- an external secret-management process that creates `leadrecovery-secrets`;
- a default `ReadWriteOnce` StorageClass for local/staging;
- a production `ReadWriteMany` StorageClass. The production placeholder is
  `shared-rwx` and must be patched to the provider-specific class before use;
- Metrics Server before enabling the committed production API HPA.

The NetworkPolicies expect the ingress controller namespace to carry
`kubernetes.io/metadata.name=ingress-nginx`. Patch the namespace selector if
the chosen controller runs elsewhere.

## Image and configuration preparation

Local overlays use `leadrecovery-{api,worker,web}:local`. Staging and production
retain `sha-replace-me` as a fail-closed source placeholder. Never apply those
overlays directly. `eng/Render-KubernetesRelease.ps1` replaces the placeholders
with three validated registry digests and writes separate migration/workload
manifests without changing the tracked overlays.

Patch the example public hosts, webhook base URL, Ingress host/TLS host, and
production storage class for the target environment. Keep fake providers,
automation, AI, and retention disabled until their separate operational gates
have been completed.

Create the namespace and secrets through the environment secret manager. This
local-only example avoids committing a Secret manifest:

```powershell
kubectl create namespace leadrecovery-local --dry-run=client -o yaml | kubectl apply -f -
$env:DATABASE_CONNECTION_STRING = 'Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require'
kubectl -n leadrecovery-local create secret generic leadrecovery-secrets `
  --from-literal=database-connection-string="$env:DATABASE_CONNECTION_STRING"
```

When real providers are enabled, the same Secret may supply
`twilio-account-sid`, `twilio-auth-token`, and `openai-api-key`. Those keys are
optional while their providers remain disabled. Never commit their values or a
generated Secret YAML file.

## Migration-first deployment

For staging, after confirming the database recovery point, namespace, TLS, and
application Secret, render a release using the exact digests returned by the
registry:

```powershell
$revision = git rev-parse HEAD
./eng/Render-KubernetesRelease.ps1 `
  -Environment staging `
  -ApiImage 'ghcr.io/<owner>/<repository>-api@sha256:<digest>' `
  -WorkerImage 'ghcr.io/<owner>/<repository>-worker@sha256:<digest>' `
  -WebImage 'ghcr.io/<owner>/<repository>-web@sha256:<digest>' `
  -PublicBaseUrl 'https://<staging-host>' `
  -ReleaseRevision $revision `
  -OutputDirectory '.artifacts/staging-release'

kubectl -n leadrecovery-staging delete job leadrecovery-migrate --ignore-not-found
kubectl apply -f .artifacts/staging-release/migration.yaml
kubectl -n leadrecovery-staging wait --for=condition=complete `
  job/leadrecovery-migrate --timeout=10m
kubectl -n leadrecovery-staging logs job/leadrecovery-migrate

kubectl apply -f .artifacts/staging-release/workloads.yaml
kubectl -n leadrecovery-staging rollout status deployment/leadrecovery-api --timeout=5m
kubectl -n leadrecovery-staging rollout status deployment/leadrecovery-worker --timeout=5m
kubectl -n leadrecovery-staging rollout status deployment/leadrecovery-web --timeout=5m
```

Use the equivalent `local` or `production` paths/namespaces. If the migration
fails, do not apply workloads. Database rollback is not automatic; use a
forward-compatible migration or the rehearsed database restore plan.

## Verification and recovery

```powershell
kubectl -n leadrecovery-staging get deployments,pods,services,pvc,ingress
kubectl -n leadrecovery-staging port-forward service/leadrecovery-api 18080:8080
Invoke-WebRequest http://localhost:18080/health/ready -UseBasicParsing
```

The API and web use `maxUnavailable: 0` rolling updates. The worker uses one
replica with `Recreate` so Hangfire owns durable queued work while Kubernetes
restarts a failed process. Production adds two API/web replicas, disruption
budgets, and an API HPA. ASP.NET Core data-protection keys are persisted on the
PVC; production replicas therefore require shared RWX storage.

## GitHub release environments

The `Release` workflow starts only for a `vMAJOR.MINOR.PATCH` tag reachable from
`main`. It publishes API, worker, and web images with full commit-SHA tags,
records their immutable digests, attaches SBOM/provenance, scans the published
digests, deploys staging migration-first, and checks both `/` and
`/api/v1/auth/csrf`. It then stops. An operator must manually dispatch
`Promote Production` with that successful Release run ID and confirm both the
staging smoke result and current database recovery point. The promotion
workflow verifies the run through the GitHub API, downloads its staging record,
and derives the identical digests from that artifact before entering the
`production` GitHub environment.

Configure both `staging` and `production` environments in repository settings:

- secret `KUBE_CONFIG_B64`: a base64-encoded least-privilege kubeconfig scoped
  to that environment's cluster/namespace;
- variable `PUBLIC_BASE_URL`: the root HTTPS origin, with no path or custom
  port;
- deployment branch/tag restrictions appropriate to the repository;
- required reviewers and prevention of self-review on `production`.

The separate manual promotion is the fail-closed approval boundary even before
environment reviewers are configured. Required production reviewers add a
second independent approval and should still be configured before pilot use.
Both production promotion and rollback refuse dispatches from any ref other
than `main`.

Before the first workflow run, externally provision the environment namespace,
`leadrecovery-secrets`, `leadrecovery-tls`, database/recovery point, ingress,
and required storage. Keep GHCR packages public or configure cluster-side image
pull credentials through the external secret process. None of those values
belong in workflow or manifest files.

Protect `main` with the four CI checks: `Backend quality gates`, `Frontend and
browser acceptance`, `API and repository policy`, and every
`Container image scan (...)` matrix result. Create and push a release tag only
after those checks pass:

```powershell
git tag v0.9.0 <validated-main-commit>
git push origin v0.9.0
```

Rendered manifests and a PII-free deployment record are retained as staging
and production workflow artifacts. A missing environment prerequisite, failed
migration, failed rollout, digest mismatch, failed scan, or smoke failure stops
promotion.

## Application rollback

Use the manual `Rollback` workflow and select `staging` or `production`. Copy
the prior full commit SHA and all three `sha256:` digests from the earlier
release summary/artifact. Confirm database compatibility only after reviewing
every migration since that release and the current recovery point. Production
rollback uses the same protected-environment reviewer gate as deployment.

Rollback renders the prior digests with current trusted tooling from `main`,
updates the three workloads, waits for each rollout, verifies the deployed
digest, and repeats the public smoke checks. It deliberately does not execute,
reverse, or delete a database migration. If the current schema is incompatible,
stop and use a forward fix or the rehearsed database restore procedure.

## Validation record

On 2026-07-28:

- all base, local, staging, production, and migration overlays rendered with
  Kustomize 5.0.4 and passed Kubernetes 1.28 server-side dry-run validation;
- an isolated kind 0.32.0/Kubernetes 1.28.13 cluster applied migrations before
  workloads and reached `Available` for API, worker, and web;
- API and worker readiness returned HTTP 200 from inside the cluster;
- deleting the worker pod produced a new Ready pod while database readiness
  remained healthy;
- changing the API pod template replaced ReplicaSet `56759cb6d5` with
  `6665f8bc5c`, and API readiness remained HTTP 200 after rollout;
- a second isolated kind test deployed digest-pinned release A migration-first,
  promoted all three workloads to release B, then reapplied only release A's
  workload manifest. API/worker/web returned to their exact A digests, API and
  web smoke checks returned HTTP 200, and the migration Job UID remained
  unchanged during rollback;
- actionlint 1.7.12 accepted all workflows, and the deterministic policy test
  reproduced the exact release A manifest after rendering A -> B -> A;
- the initial shared RWX base claim failed on the local provisioner, leading to
  the portable RWO base plus explicit production RWX overlay now committed.

The first kind attempt used Kubernetes 1.36 and failed on the installed older
Docker Desktop/cgroup-v1 engine. Retesting with the locally supported
Kubernetes 1.28 line passed. This is an environment compatibility note, not a
workload failure.

Run `eng/Test-DeploymentArtifacts.ps1` and `eng/Test-CiCdArtifacts.ps1` after
any deployment/workflow edit. The latter renders release A, release B, and A
again, proving that prior digests reproduce the exact prior workload manifest.
