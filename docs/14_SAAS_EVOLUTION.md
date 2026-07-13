# 14 - Productization and SaaS Evolution

## 1. When not to build SaaS

Do not build a public self-service SaaS merely because the demo works. Continue service-led delivery until repeated paid evidence exists.

Minimum signals before SaaS investment:

- at least 3 paying clients with the same core problem;
- preferably 5 similar installations;
- repeated use of the same workflow and configuration;
- customers willing to pay recurring fees;
- known onboarding steps;
- known support burden;
- stable provider integration pattern;
- evidence that customization can be bounded.

## 2. Productization stages

### Stage 1 - Custom pilot

- one tenant;
- manual onboarding;
- configuration may require administrator action;
- narrow workflow.

### Stage 2 - Productized service

- reusable tenant configuration;
- standard packages;
- repeatable deployment;
- template library;
- monthly monitoring;
- onboarding checklist;
- limited supported integrations.

### Stage 3 - Managed multi-tenant platform

- self-service user invitations;
- tenant admin settings;
- usage metering;
- billing handled manually or through a simple subscription integration;
- standardized support.

### Stage 4 - Niche SaaS

- self-service trial/onboarding where economically justified;
- guided Twilio/phone connection;
- subscription billing;
- standard dashboards;
- documented integration marketplace strategy;
- stronger SLOs and support automation.

## 3. Feature gates for later phases

Potential later features:

- quote follow-up sequences;
- review requests;
- dormant-customer reactivation;
- web-form lead ingestion;
- CRM integrations;
- calendar booking webhooks;
- call transcription where consent and value justify it;
- multi-location support;
- agency partner dashboard;
- white-label branding;
- usage billing;
- template marketplace.

Each feature requires paid validation or strong operational evidence.

## 4. Architecture evolution triggers

### Add Redis when

- PostgreSQL-backed job/caching load becomes measurable bottleneck;
- real-time fan-out requires it;
- load tests justify operational complexity.

### Split a service when

- independent scaling or reliability is proven;
- deployment coupling causes material incidents;
- a separate team owns it;
- data boundaries are stable.

Possible future extraction order:

1. messaging/notification service;
2. integration webhook gateway;
3. reporting pipeline.

Do not split core lead workflow early.

### Add event streaming when

- durable event consumers multiply;
- analytics/reporting cannot be served safely from transactional DB;
- replay and independent processing provide real value.

## 5. Billing design later

Possible model:

- setup fee;
- monthly platform fee;
- included message allowance;
- usage overage;
- agency plan.

Before automated billing, understand provider pass-through costs, support time, and gross margin.

## 6. SaaS readiness checklist

- tenant isolation independently reviewed;
- onboarding can be completed without developer intervention;
- standard support documentation;
- automated backup/restore testing;
- incident response process;
- privacy terms and provider agreements;
- subscription lifecycle;
- usage metering accuracy;
- abuse prevention;
- cancellation/export/deletion flow;
- reliable migration process;
- product analytics separated from customer content.

## 7. Long-term portfolio narrative

The project demonstrates a credible progression:

1. Identify a measurable business problem.
2. Build a C# webhook-driven workflow.
3. Add reliable background processing and human control.
4. Containerize and deploy through Kubernetes.
5. Validate with real service businesses.
6. Extract a repeatable niche product only after evidence.

This is stronger than presenting Kubernetes or AI as disconnected technical exercises.
