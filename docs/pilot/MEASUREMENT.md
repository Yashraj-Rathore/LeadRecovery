# Pilot measurement plan

Agree the baseline, evaluation window, and success criteria with the pilot business before activation. The report is tenant-scoped, defaults to the latest 30 inclusive UTC dates, accepts a maximum 366-day range, and is available in the workspace at `/reports/pilot`, as JSON at `/api/v1/reports/pilot`, and as CSV at `/api/v1/reports/pilot.csv`.

## Baseline recorded before launch

- evaluation start/end and business timezone;
- inbound calls and recoverable missed calls per week;
- current median time to first response and how it was sampled;
- missed callers who reply to the existing process;
- staff-confirmed qualified and booked outcomes;
- staff minutes spent on missed-call follow-up, with sampling method;
- opt-outs, complaints, provider failures, and manual interventions;
- owner responsible for confirming booking attribution.

## Implemented fields

| Field | Definition |
|---|---|
| Missed calls | Missed-call Leads created in the selected UTC range; denominator for displayed rates |
| Recovery sent/delivered | Initial approved recovery messages with provider state `Sent` or `Delivered` |
| Leads with inbound reply | Distinct baseline Leads with an inbound message in the range |
| Reply rate | Leads with inbound reply divided by missed calls |
| Qualified Leads | Baseline Leads currently Qualified, Booking Offered, Booked, or Closed Won |
| Booked Leads | Baseline Leads with a staff-confirmed booked timestamp |
| Booking rate | Booked Leads divided by missed calls; operational only |
| Manual messages | Staff-authored outbound messages that reached Sent or Delivered in the range |
| Failed messages | Messages in provider failure state |
| Opt-outs | `CustomerSmsOptedOut` audit decisions in the range |
| Needs human review | Baseline Leads currently requiring a person |
| Median first response | Median minutes from Lead creation to first sent initial recovery message |

## Suggested pilot agreement

Replace these starter thresholds with jointly agreed values: at least 95% of eligible missed calls create one recovery intent; at least 95% of attempted initial messages reach Sent or Delivered; no duplicate Lead/message effect from a duplicate callback; every STOP blocks later automated sends; median first response is below the agreed business target; staff reviews the queue on the agreed cadence; and every failure or complaint has an owner. Reply and booking movement are learning signals, not guaranteed pass/fail outcomes for a small sample.

Review the export weekly with the pilot owner. Annotate outages, routing changes, unusual call volume, staff absences, and sample-size limitations. Do not translate bookings into revenue or claim incremental causation without an agreed comparison method and staff-confirmed attribution.
