# ADR-0023: Production images and Kubernetes rollout

- Status: Accepted
- Date: 2026-07-28

## Context

LR-0901 and LR-0902 require one reproducible deployment model for the existing
API, worker, web dashboard, and PostgreSQL schema. The model must preserve the
modular monolith, keep credentials out of Git, avoid replica startup races with
database migrations, and work on a small local/staging cluster before CI/CD is
implemented in LR-0903.

The originally reserved Node.js 24.17.0 image is not published. The current
supported Node.js 24.18.0 Bookworm image and current .NET 10 SDK/runtime patch
images are published and were verified through their immutable registry
digests.

## Decision

1. Build separate API, worker, and web runtime images with multi-stage
   Dockerfiles. Pin every base by exact version and multi-platform digest.
2. Run .NET workloads as the image `app` user and the dashboard as `node`.
   Include OCI version/revision/creation labels and an OCI health check.
3. Reuse the API image for an explicit `--migrate` command. Normal API replicas
   never migrate at startup.
4. Host internal liveness and database readiness endpoints in the worker so
   both Docker and Kubernetes can distinguish a live process from usable job
   storage.
5. Use Docker Compose for the complete local stack. PostgreSQL health gates the
   one-shot migration service; successful migration gates API/worker startup;
   API health gates dashboard startup.
6. Use plain Kustomize bases and local, staging, and production overlays.
   Migration overlays remain separate and must complete before workload
   overlays are applied.
7. Keep PostgreSQL external to Kubernetes. Workloads receive the database
   connection and optional provider credentials only through Secret references.
8. Run all workloads with non-root, read-only, no-privilege-escalation, dropped-
   capability, resource, probe, and dedicated ServiceAccount controls. Restrict
   inbound pod traffic to the web/API paths required by ingress and the
   same-namespace dashboard proxy.
9. Persist ASP.NET Core data-protection keys. The one-replica local/staging base
   uses broadly available RWO storage. The two-replica production overlay
   requires an operator-supplied RWX `shared-rwx` StorageClass.
10. Keep automation, real SMS, AI, retention, and demo seeding disabled in the
    deployment baseline. Environment operators enable each only after its
    independent safety gate.
11. Update the repository Node.js pin from unavailable 24.17.0 to published
    24.18.0. Application .NET package locks remain at 10.0.9 while images use
    the compatible current .NET 10 SDK 10.0.302 and runtime 10.0.10 patches.

## Consequences

Compose and Kubernetes execute the same application artifacts and migration
path. A failed migration prevents the documented workload rollout rather than
racing API/worker replicas. Deployments can roll or restart without losing
database-backed workflow state, and browser cookies share protected key
material across API replicas.

The Kubernetes deployment requires environment-specific ingress, TLS, external
secret management, database connectivity, and production RWX storage. The
committed production image tag and host values are placeholders and are not a
release. LR-0903 must replace them with immutable release values, add an
authenticated image scan/SBOM gate, and automate staged rollout and rollback.
