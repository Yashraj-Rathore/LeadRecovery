# Tenant onboarding runbook

LR-1001 uses a versioned JSON plan plus password environment variables. A trusted platform operator can configure the business, phone, hours, qualification, follow-ups, booking link, approved templates, initial users, and opt-in retention policy without changing code. No password is stored in the plan.

## Before activation

- [ ] Signed scope, responsibilities, support contacts, launch date, and rollback owner recorded.
- [ ] Business name, unique slug, installed IANA timezone, and business hours confirmed; any pilot service-area rule is documented in the qualification/staff process because it is not a stored tenant setting yet.
- [ ] Test or production provider number, provider number ID, missed-call statuses, delay, and cooldown confirmed.
- [ ] Customer consent and STOP wording approved by the business.
- [ ] Qualification questions, follow-up timing, and booking/callback route approved.
- [ ] Every required SMS purpose has one reviewed template.
- [ ] Initial users and least-privilege roles approved; exactly one initial Owner identified.
- [ ] Password secrets supplied through operator environment variables.
- [ ] Retention remains disabled or its 30-3,650 day policy and backup/restore owner are approved.
- [ ] Database migration, backup/recovery point, fake-provider test, and disable procedure confirmed.
- [ ] Pilot baseline and success criteria from [MEASUREMENT.md](MEASUREMENT.md) agreed.

## Validate and activate

Run from the repository root. Copy the fictional template to the ignored local filename and edit business configuration only:

```powershell
Copy-Item templates/tenant-onboarding.example.json tenant-onboarding.local.json
$env:ONBOARD_OWNER_PASSWORD = '<strong unique password>'
$env:ONBOARD_STAFF_PASSWORD = '<strong unique password>'
$env:ConnectionStrings__Database = '<operator database connection>'

dotnet run --project src/LeadRecovery.Api --configuration Release --no-launch-profile -- --validate-onboarding tenant-onboarding.local.json
dotnet run --project src/LeadRecovery.Api --configuration Release --no-launch-profile -- --onboard tenant-onboarding.local.json
```

Validation is read-only. Activation runs in a serializable transaction: the Tenant begins in `Trial`, all identity/membership/phone/workflow/template records must persist, and only then does the Tenant become `Active`. A uniqueness or Identity failure rolls the transaction back and returns structured errors. Automation defaults to disabled and should remain disabled until post-activation checks pass.

## After activation

- [ ] Owner and Staff can authenticate and see only their tenant.
- [ ] Phone routing and signed callback URL validated with the test number.
- [ ] Missed call creates one Lead and one recovery intent.
- [ ] Initial SMS wording, delivery callback, reply capture, and timeline checked.
- [ ] Duplicate callback proof and STOP proof pass.
- [ ] Business hours, booking destination, follow-up cancellation, and human handoff checked.
- [ ] Pilot report range and CSV export checked by the pilot owner.
- [ ] Automation enabled only after the business approves the completed checklist.

## Support, pause, and rollback

An Owner or Manager can pause tenant automation from the workspace header. An operator can set `AUTOMATION_GLOBAL_ENABLED=false` in both API and Worker and restart them for the platform kill switch. Both controls preserve authentication, dashboard access, inbound capture, and manual staff messaging while cancelling eligible queued automated work. For routing or consent incidents, disable provider missed-call routing as well. Do not delete the tenant as a rollback mechanism; preserve records for investigation, log the incident without PII, and follow the recovery procedures in `docs/10_OBSERVABILITY_OPERATIONS.md`.
