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
    phone_e164 varchar(16) not null,
    name varchar(200) null,
    email varchar(320) null,
    city varchar(100) null,
    postal_code varchar(20) null,
    sms_consent_basis varchar(100) null,
    opted_out_at_utc timestamptz null,
    created_at_utc timestamptz not null,
    unique(tenant_id, phone_e164),
    unique(tenant_id, id)
);

create table leads (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    customer_id uuid null,
    primary_phone_e164 varchar(32) not null,
    display_name varchar(200) null,
    source varchar(32) not null,
    status varchar(32) not null,
    urgency varchar(32) not null,
    service_category_id uuid null,
    assigned_user_id uuid null,
    automation_state varchar(32) not null,
    last_customer_activity_at_utc timestamptz null,
    last_business_activity_at_utc timestamptz null,
    booked_at_utc timestamptz null,
    closed_at_utc timestamptz null,
    close_reason varchar(32) null,
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
create index ix_leads_tenant_assigned_status
    on leads(tenant_id, assigned_user_id, status);
create index ix_leads_tenant_urgency_status
    on leads(tenant_id, urgency, status);

create table conversations (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    channel varchar(16) not null,
    status varchar(16) not null,
    created_at_utc timestamptz not null,
    closed_at_utc timestamptz null,
    unique(tenant_id, id),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id)
);

create index ix_conversations_tenant_lead_created
    on conversations(tenant_id, lead_id, created_at_utc desc);

create table messages (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    conversation_id uuid not null,
    direction varchar(16) not null,
    kind varchar(16) not null,
    provider varchar(50) not null,
    provider_message_sid varchar(100) null,
    client_idempotency_key varchar(200) not null,
    body varchar(1600) not null,
    status varchar(16) not null,
    failure_code varchar(100) null,
    failure_description varchar(500) null,
    sent_by_user_id uuid null,
    template_id uuid null,
    created_at_utc timestamptz not null,
    sent_at_utc timestamptz null,
    delivered_at_utc timestamptz null,
    unique(tenant_id, client_idempotency_key),
    unique(tenant_id, id),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id),
    foreign key (tenant_id, conversation_id) references conversations(tenant_id, id)
);

create unique index ux_messages_provider_sid
    on messages(provider, provider_message_sid)
    where provider_message_sid is not null;

create index ix_messages_tenant_conversation_created
    on messages(tenant_id, conversation_id, created_at_utc);

create table external_event_receipts (
    id uuid primary key,
    tenant_id uuid null references tenants(id),
    provider varchar(50) not null,
    event_type varchar(100) not null,
    external_event_id varchar(200) not null,
    payload_hash varchar(128) not null,
    received_at_utc timestamptz not null,
    processed_at_utc timestamptz null,
    processing_result varchar(500) null,
    unique(provider, event_type, external_event_id)
);

create index ix_external_event_receipts_received_at
    on external_event_receipts(received_at_utc);

create table scheduled_actions (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    lead_id uuid not null,
    action_type varchar(100) not null,
    scheduled_for_utc timestamptz not null,
    status varchar(16) not null
        check (status in ('Pending', 'Running', 'Completed', 'Cancelled', 'Failed')),
    attempt_count integer not null default 0 check (attempt_count >= 0),
    idempotency_key varchar(200) not null,
    payload_json jsonb not null,
    last_error varchar(1000) null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    unique(tenant_id, idempotency_key),
    unique(tenant_id, id),
    foreign key (tenant_id, lead_id) references leads(tenant_id, id)
);

create index ix_scheduled_actions_due
    on scheduled_actions(status, scheduled_for_utc);

create index ix_scheduled_actions_tenant_lead_status
    on scheduled_actions(tenant_id, lead_id, status);

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
