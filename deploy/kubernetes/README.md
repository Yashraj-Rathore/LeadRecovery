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
contain `sha-replace-me`; replace every image tag in both the workload and
migration overlays with the same immutable release SHA before deployment.

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

For staging, after confirming the database recovery point and secret:

```powershell
kubectl -n leadrecovery-staging delete job leadrecovery-migrate --ignore-not-found
kubectl apply -k deploy/kubernetes/staging/migration
kubectl -n leadrecovery-staging wait --for=condition=complete `
  job/leadrecovery-migrate --timeout=10m
kubectl -n leadrecovery-staging logs job/leadrecovery-migrate

kubectl apply -k deploy/kubernetes/staging
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
- the initial shared RWX base claim failed on the local provisioner, leading to
  the portable RWO base plus explicit production RWX overlay now committed.

The first kind attempt used Kubernetes 1.36 and failed on the installed older
Docker Desktop/cgroup-v1 engine. Retesting with the locally supported
Kubernetes 1.28 line passed. This is an environment compatibility note, not a
workload failure.

Run `eng/Test-DeploymentArtifacts.ps1` after any deployment edit for the
repeatable local structural gate.
