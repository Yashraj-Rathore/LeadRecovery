# 05 - Frontend and UX Specification

## 1. UX objective

The dashboard must help a busy office manager answer three questions immediately:

1. Which leads need attention now?
2. What has already been said or sent?
3. What action should I take next?

The product should feel like an operational inbox, not a complex CRM.

## 2. Information architecture

Primary navigation:

- Inbox
- All Leads
- Reports
- Settings
- Users
- System Status (Owner/Manager)

For the current pilot, Inbox and All Leads are one filterable `/leads`
workspace, Reports is `/reports/pilot`, and tenant automation control is in the
shared workspace header. Settings, user administration, password recovery, and
a separate System Status screen are later productized-service surfaces; their
absence is intentional and no dead navigation is shown. A trusted operator
uses the validated onboarding command for initial configuration and users.

## 3. Core screens

### 3.1 Login

Requirements:

- email and password;
- forgot password;
- clear error messages without revealing account existence unnecessarily;
- accessible labels and focus order;
- rate-limit feedback.

Milestone 2 implements the email/password form with accessible labels, disabled
submitting state, generic credential errors, explicit rate-limit feedback, and
same-origin CSRF initialization. Forgot/reset password is intentionally deferred
with its API workflow; no dead control is shown.

### 3.2 Lead inbox

Default filters:

- Needs Human
- New
- Urgent
- Unassigned

Columns/cards:

- customer name or phone;
- service category;
- urgency;
- source;
- status;
- assigned user;
- age since last customer activity;
- automation indicator;
- unread indicator.

Actions:

- open lead;
- assign to self;
- mark spam;
- bulk actions are out of MVP scope except safe assignment/filter operations.

Milestone 5 completes the operational slice: tenant-scoped status, urgency,
assignment, and exact-user filters; lead navigation; assignment; unread and
automation indicators; loading/empty/retry states; and manual refresh plus
ten-second polling. Semantic labels, ordinary selects, visible focus, and
44-pixel action targets support keyboard use. A PostgreSQL integration
acceptance test measures the filtered endpoint with 10,000 tenant Leads.

### 3.3 Lead detail

Desktop layout:

```text
+--------------------------------------------------------------+
| Customer / Status / Urgency / Assignment / Automation        |
+-------------------------------+------------------------------+
| Conversation timeline         | Lead details                 |
| SMS bubbles, call events,      | service, location, source,   |
| system events                  | booking, summary, notes      |
|                               |                              |
| Message composer              | Next actions                 |
+-------------------------------+------------------------------+
```

Required controls:

- send manual SMS;
- pause/resume automation;
- assign;
- transition status;
- edit category and urgency;
- accept/edit AI summary;
- add note;
- copy phone number;
- open booking link;
- view pending follow-ups and cancel them.

Milestone 5 implemented the controls owned by LR-0501 through LR-0505: manual
SMS, pause/resume, assignment, domain-allowed transitions, internal notes, copy
phone, and pending-action display. Milestone 6 adds structured qualification
answers and the current unanswered prompt, the approved booking destination,
booking-link queueing for active Qualified Leads, and cancellation buttons for
Pending actions. Marking `Booked` removes pending automated follow-ups from the
view after the server transaction. Direct Lead category/urgency editing remains
a later issue.

A pre-LR-0702 visual and usability refresh applies one tokenized interface
system to login, inbox, and Lead detail without adding new product navigation or
changing workflow behavior. The refresh prioritizes human-review/unread rows,
replaces raw enum values with staff-readable labels, provides explicit polling
and mutation feedback, separates inbound/outbound timeline messages, and adds
consistent global error, not-found, empty, and skeleton states. Desktop, tablet,
and 390-pixel mobile layouts preserve essential controls without horizontal
overflow; visible controls meet the 44 CSS-pixel target, focus treatment uses a
high-contrast outline, and reduced-motion and increased-contrast preferences
are respected.

LR-0802 adds a compact, high-contrast automation status to the shared workspace
header. Every role can see `Automation on`, `Tenant paused`, `Platform paused`,
or the fail-safe unknown state. Owner and Manager members can open the control,
review its impact, and pause or resume tenant automation; Staff and ReadOnly
members receive status-only presentation. Mutation feedback reports cancelled
queued work, concurrency conflicts trigger a fresh status read, and the copy
explicitly confirms that inbound capture, the dashboard, and manual staff
messages remain available.

LR-0702 adds a prominent responsive review card before the conversation/action
grid whenever analyses exist. It always says that content is AI-generated,
shows confidence as a percentage plus text, and gives sub-65% suggestions a
human-review warning that is not color-only. Owner, Manager, and Staff may
accept, edit all structured staff-facing values, optionally explain a
correction, or reject. ReadOnly users can inspect the result without controls.
The suggested reply is labeled as an unsent draft, and the review footer states
that no review action sends or schedules customer communication.

### 3.4 Settings - Business

Planned self-service screen; operator-managed during the current pilot.

- business name;
- timezone;
- business hours;
- service area;
- booking URL;
- notification recipients.

### 3.5 Settings - Message templates

Planned self-service screen; templates are versioned, approved, and activated
transactionally by the current onboarding command.

- list versions;
- preview substitutions;
- create draft;
- approve/activate;
- test-send to an authorized test number;
- character/segment estimate;
- required opt-out language warning.

### 3.6 Settings - Automation

Planned full settings screen. The current workspace header exposes the safe
tenant pause/resume subset to Owner and Manager users.

- global enable/disable;
- recoverable call statuses;
- cooldown period;
- initial delay;
- follow-up schedule;
- after-hours behavior;
- qualification questions;
- AI feature toggles.

### 3.7 Reports

The current `/reports/pilot` screen implements the bounded operational metrics
defined in `docs/pilot/MEASUREMENT.md` and matching JSON/CSV exports. The
broader dashboard card set below remains a future analytics surface.

MVP cards:

- missed calls;
- recovery messages;
- reply rate;
- median response time;
- booked leads;
- needs-human backlog;
- failed messages.

Include date range and timezone note.

## 4. Visual priority

- Urgent human-review leads appear first.
- Red is reserved for failures or critical attention, not ordinary status.
- Automation state must include text/icon, not color only.
- Failed outbound messages display a clear reason and safe retry option.

## 5. Responsive behavior

The dashboard must work on laptop and tablet. Mobile web should support lead viewing and essential actions but need not provide full configuration editing in MVP.

## 6. Accessibility requirements

- semantic HTML;
- keyboard-accessible navigation and dialogs;
- focus returns correctly after modal close;
- ARIA live region for new-message/update notification where appropriate;
- no inaccessible custom select controls;
- form errors linked to fields;
- minimum 44x44 CSS pixel touch targets for primary actions;
- timestamps readable by screen readers;
- charts have text summaries.

## 7. Loading and error states

Every screen must define:

- initial loading;
- empty state;
- permission denied;
- recoverable network error;
- stale update/concurrency conflict;
- partial integration failure.

Example concurrency message:

> This lead changed while you were viewing it. Review the latest status before trying again.

## 8. Frontend technical approach

- Next.js with TypeScript.
- Server/client boundaries chosen deliberately; do not expose secrets to browser bundles.
- API client generated from OpenAPI or strongly typed manually.
- TanStack Query or equivalent for server state.
- React Hook Form plus schema validation for forms.
- Component library allowed, but accessibility must be verified.
- Use a small design-token set rather than ad hoc styles.

## 9. Real-time strategy

MVP may poll lead counts and open conversations every 5-10 seconds. SignalR can replace polling after core flows are stable.

The implemented inbox polls every ten seconds and an open Lead every eight.
Composer and note drafts remain local state. If new activity arrives while the
message composer has focus, an ARIA-live notification appears without replacing
the draft.

When a new message arrives:

- inbox count updates;
- open lead timeline updates;
- staff typing must not be silently overwritten;
- provide a visible "new activity" indicator if auto-scroll would be disruptive.

## 10. Demo mode

Provide a safe demo seed mode with fictional data. It must never send real SMS unless a specific environment flag and approved test number are configured.

Demo data should show:

- one urgent plumbing lead;
- one normal booking request;
- one opted-out contact;
- one failed message;
- one booked lead;
- one duplicate webhook handled successfully.
