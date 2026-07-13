-- Reference schema only. EF Core migrations are the implementation source of truth.

create table tenants (
    id uuid primary key,
    name text not null,
    slug text not null unique,
    timezone_id text not null,
    status text not null,
    automation_enabled boolean not null default false,
    version bigint not null default 0,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null
);

create table tenant_phone_numbers (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    provider text not null,
    phone_number_e164 text not null,
    provider_number_sid text not null,
    inbound_sms_enabled boolean not null default true,
    missed_call_recovery_enabled boolean not null default true,
    is_primary boolean not null default false,
    unique(provider, provider_number_sid),
    unique(tenant_id, phone_number_e164)
);

create table customers (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    phone_e164 text not null,
    name text null,
    email text null,
    city text null,
    postal_code text null,
    sms_consent_basis text null,
    opted_out_at_utc timestamptz null,
    created_at_utc timestamptz not null,
    unique(tenant_id, phone_e164),
    unique(tenant_id, id)
);

create table leads (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    customer_id uuid null,
    primary_phone_e164 text not null,
    display_name text null,
    source text not null,
    status text not null,
    urgency text not null,
    service_category_id uuid null,
    assigned_user_id uuid null,
    automation_state text not null,
    last_customer_activity_at_utc timestamptz null,
    last_business_activity_at_utc timestamptz null,
    booked_at_utc timestamptz null,
    closed_at_utc timestamptz null,
    close_reason text null,
    version bigint not null default 0,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    unique(tenant_id, id),
    foreign key (tenant_id, customer_id) references customers(tenant_id, id)
);

create index ix_leads_tenant_status_created
    on leads(tenant_id, status, created_at_utc desc);
create index ix_leads_tenant_phone_created
    on leads(tenant_id, primary_phone_e164, created_at_utc desc);

create table conversations (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    channel text not null,
    status text not null,
    created_at_utc timestamptz not null,
    closed_at_utc timestamptz null,
    unique(tenant_id, id),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id)
);

create table messages (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    conversation_id uuid not null,
    direction text not null,
    kind text not null,
    provider text not null,
    provider_message_sid text null,
    client_idempotency_key text not null,
    body text not null,
    status text not null,
    failure_code text null,
    failure_description text null,
    sent_by_user_id uuid null,
    template_id uuid null,
    created_at_utc timestamptz not null,
    sent_at_utc timestamptz null,
    delivered_at_utc timestamptz null,
    unique(tenant_id, client_idempotency_key),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id),
    foreign key (tenant_id, conversation_id) references conversations(tenant_id, id)
);

create unique index ux_messages_provider_sid
    on messages(provider, provider_message_sid)
    where provider_message_sid is not null;

create table external_event_receipts (
    id uuid primary key,
    tenant_id uuid null references tenants(id),
    provider text not null,
    event_type text not null,
    external_event_id text not null,
    payload_hash text not null,
    received_at_utc timestamptz not null,
    processed_at_utc timestamptz null,
    processing_result text null,
    unique(provider, event_type, external_event_id)
);

create table scheduled_actions (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    action_type text not null,
    scheduled_for_utc timestamptz not null,
    status text not null,
    attempt_count integer not null default 0,
    idempotency_key text not null,
    payload_json jsonb not null,
    last_error text null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    unique(tenant_id, idempotency_key),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id)
);

create index ix_scheduled_actions_due
    on scheduled_actions(status, scheduled_for_utc);

create table audit_events (
    id uuid primary key,
    tenant_id uuid null references tenants(id),
    actor_type text not null,
    actor_id text null,
    action text not null,
    entity_type text not null,
    entity_id text not null,
    before_json jsonb null,
    after_json jsonb null,
    correlation_id text not null,
    created_at_utc timestamptz not null
);
