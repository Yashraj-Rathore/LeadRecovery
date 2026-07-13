# 13 - Pilot, Demo, and Market Validation Plan

## 1. Commercial positioning

Do not sell “AI software.” Sell a concrete operational result:

> We help plumbing businesses respond to missed callers automatically, collect the basic job details, and direct qualified customers to booking or a callback.

## 2. Initial offer

### Workflow audit

Deliverables:

- 30-minute workflow review;
- map of current call and follow-up process;
- list of lost-lead risks;
- one recommended pilot workflow;
- short recorded summary.

Suggested early price: C$149-C$249.

### Starter pilot

Deliverables:

- one phone number/workflow;
- missed-call SMS;
- up to three qualification questions;
- booking/callback path;
- lead dashboard;
- staff notifications;
- logs and manual override;
- 14-30 days of support;
- final results review.

Suggested first-client price: C$750-C$1,250 plus usage costs.

## 3. Two-minute demo script

1. Show fictional plumbing company dashboard with no active lead.
2. Call test Twilio number and do not answer.
3. Show automatic SMS received.
4. Reply: “There is water leaking under my kitchen sink in Mississauga.”
5. Show lead appear with category and urgency suggestion.
6. Show staff send or approve callback/booking message.
7. Mark lead Booked.
8. Show pending follow-up cancelled.
9. Briefly show audit trail and Kubernetes deployment diagram.

The demo should emphasize outcome and reliability, not YAML or model hype.

## 4. Validation before custom build

Ask prospects:

- How many calls are missed in a normal week?
- What happens after a missed call?
- How quickly does someone usually respond?
- How are quote follow-ups handled?
- Which tools already manage calls, CRM, and booking?
- What would one additional booked job per week be worth?
- What messaging would customers expect?
- What would make the business distrust automation?
- Who must approve messages and workflow changes?

## 5. Seven-day validation sprint

Day 1: choose plumbing as the niche and missed-call recovery as the only offer.

Day 2: prepare landing page and demo architecture.

Day 3: build/record a clickable or functional demo.

Day 4: create a list of 50 Ontario plumbing companies and 20 digital agencies/MSPs serving trades.

Day 5: send 30 targeted messages.

Day 6: send 30 more messages, five agency partnership messages, and three narrow freelance proposals.

Day 7: review replies and objections.

Pass signal after roughly 100 targeted contacts:

- 8-12 replies;
- 4-6 substantive conversations;
- at least one paid audit or clear pilot commitment.

If interest exists but nobody pays, narrow scope or lower risk rather than adding features.

## 6. Pilot onboarding checklist

- signed scope and responsibilities;
- tenant business details;
- approved users and roles;
- phone-routing design;
- test/production Twilio configuration;
- approved SMS templates;
- service categories and area;
- business hours and follow-up policy;
- booking/callback process;
- opt-out and consent process;
- support contacts;
- measurement baseline;
- launch date and rollback plan.

## 7. Pilot launch stages

### Stage A - Internal test

- test number only;
- fictional/test data;
- owner and office manager verify wording.

### Stage B - Limited live

- one number or time window;
- close monitoring;
- daily review;
- manual override ready.

### Stage C - Full pilot

- agreed hours and call flows;
- weekly results review;
- defects and requested changes logged separately.

## 8. Pilot metrics

Measure:

- total recoverable missed calls;
- recovery SMS sent/delivered;
- replies;
- qualified leads;
- booking/callback requests;
- booked leads where staff confirms attribution;
- median response time;
- manual interventions;
- failures;
- opt-outs;
- staff time saved estimate with method stated.

Do not claim all bookings were caused by the system without a reasonable attribution method.

## 9. Pilot exit review

Questions:

- Did customers reply?
- Did staff use the dashboard?
- Which questions caused confusion?
- Which integrations were actually required?
- What failed or needed manual work?
- Did the business want to keep paying?
- Which features repeated across prospects?

## 10. Case-study structure

- customer type, anonymized if needed;
- initial workflow and measurable problem;
- scope of pilot;
- architecture at a high level;
- safeguards;
- before/after operational metrics;
- limitations;
- testimonial with permission;
- next phase.

## 11. Agency white-label path

Target:

- web agencies;
- marketing firms serving trades;
- MSPs;
- CRM consultants;
- call-answering providers.

Offer:

- implementation under their brand;
- fixed technical scope;
- clear handoff and support boundaries;
- no direct solicitation of their client;
- reusable monthly capacity package after trust is established.
