using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    agency_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    agency_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agency_state = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    linkedin_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    confidence_score = table.Column<float>(type: "real", nullable: false),
                    is_opted_out = table.Column<bool>(type: "boolean", nullable: false),
                    is_bounced = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bounced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bounced_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oidc_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    authority = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    client_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    client_secret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    email_claim = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_claim = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    agency_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    agency_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agency_state = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    estimated_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    posted_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    response_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    naics_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    procurement_vehicle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    score_total = table.Column<int>(type: "integer", nullable: true),
                    score_verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    scored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    score_recompete_detected = table.Column<bool>(type: "boolean", nullable: true),
                    score_lane_title = table.Column<int>(type: "integer", nullable: true),
                    score_lane_description = table.Column<int>(type: "integer", nullable: true),
                    score_agency_tier = table.Column<int>(type: "integer", nullable: true),
                    score_win_themes = table.Column<int>(type: "integer", nullable: true),
                    score_past_performance = table.Column<int>(type: "integer", nullable: true),
                    score_procurement_vehicle = table.Column<int>(type: "integer", nullable: true),
                    score_seat_count = table.Column<int>(type: "integer", nullable: true),
                    score_recompete = table.Column<int>(type: "integer", nullable: true),
                    estimated_seats = table.Column<int>(type: "integer", nullable: true),
                    seat_estimate_confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    watched_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_amended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    encrypted_api_key = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reply_to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    physical_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unsubscribe_base_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    encrypted_webhook_secret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_configurations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outreach_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_send_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paused_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outreach_enrollments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outreach_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opportunity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agency_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outreach_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outreach_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_template = table.Column<string>(type: "text", nullable: false),
                    body_template = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outreach_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rag_memories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_memories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sequence_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequence_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adapter_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parameters = table.Column<string>(type: "text", nullable: false),
                    schedule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_run_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppression_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppression_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    password_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    totp_secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    totp_enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_contacts",
                columns: table => new
                {
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_contacts", x => new { x.opportunity_id, x.contact_id });
                    table.ForeignKey(
                        name: "FK_opportunity_contacts_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sequence_steps",
                columns: table => new
                {
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    delay_days = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    send_window_start = table.Column<TimeSpan>(type: "interval", nullable: false),
                    send_window_end = table.Column<TimeSpan>(type: "interval", nullable: false),
                    jitter_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequence_steps", x => new { x.sequence_id, x.step_number });
                    table.ForeignKey(
                        name: "FK_sequence_steps_outreach_sequences_sequence_id",
                        column: x => x.sequence_id,
                        principalTable: "outreach_sequences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_tenant_id_entity_type_entity_id",
                table: "audit_events",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_tenant_id_event_type",
                table: "audit_events",
                columns: new[] { "tenant_id", "event_type" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_tenant_id_occurred_at",
                table: "audit_events",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_tenant_id_email",
                table: "contacts",
                columns: new[] { "tenant_id", "email" },
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_contacts_tenant_id_is_opted_out",
                table: "contacts",
                columns: new[] { "tenant_id", "is_opted_out" });

            migrationBuilder.CreateIndex(
                name: "IX_email_activities_tenant_id_contact_id_sent_at",
                table: "email_activities",
                columns: new[] { "tenant_id", "contact_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_activities_tenant_id_message_id",
                table: "email_activities",
                columns: new[] { "tenant_id", "message_id" },
                filter: "message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_tokens_token_hash",
                table: "email_verification_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_tokens_user_id",
                table: "email_verification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_market_profiles_tenant_id_name",
                table: "market_profiles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oidc_configs_tenant_id_provider_key",
                table: "oidc_configs",
                columns: new[] { "tenant_id", "provider_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_tenant_id_source_definition_id_external_id",
                table: "opportunities",
                columns: new[] { "tenant_id", "source_definition_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_tenant_id_status",
                table: "opportunities",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_tenant_id_watched_since",
                table: "opportunities",
                columns: new[] { "tenant_id", "watched_since" },
                filter: "watched_since IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_configurations_tenant_id",
                table: "outbound_configurations",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outreach_enrollments_tenant_id_contact_id_opportunity_id",
                table: "outreach_enrollments",
                columns: new[] { "tenant_id", "contact_id", "opportunity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outreach_enrollments_tenant_id_status_next_send_at",
                table: "outreach_enrollments",
                columns: new[] { "tenant_id", "status", "next_send_at" });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_rag_memories_tenant_id_entity_type_entity_id",
                table: "rag_memories",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_source_definitions_tenant_id_is_enabled",
                table: "source_definitions",
                columns: new[] { "tenant_id", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_source_definitions_tenant_id_name",
                table: "source_definitions",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppression_entries_tenant_id_value",
                table: "suppression_entries",
                columns: new[] { "tenant_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_tenants_tenant_id_status",
                table: "user_tenants",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_user_tenants_user_id_tenant_id",
                table: "user_tenants",
                columns: new[] { "user_id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "email_activities");

            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "market_profiles");

            migrationBuilder.DropTable(
                name: "oidc_configs");

            migrationBuilder.DropTable(
                name: "opportunity_contacts");

            migrationBuilder.DropTable(
                name: "outbound_configurations");

            migrationBuilder.DropTable(
                name: "outreach_enrollments");

            migrationBuilder.DropTable(
                name: "outreach_templates");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "rag_memories");

            migrationBuilder.DropTable(
                name: "sequence_snapshots");

            migrationBuilder.DropTable(
                name: "sequence_steps");

            migrationBuilder.DropTable(
                name: "source_definitions");

            migrationBuilder.DropTable(
                name: "suppression_entries");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "user_tenants");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropTable(
                name: "outreach_sequences");
        }
    }
}
