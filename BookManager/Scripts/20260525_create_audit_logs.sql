CREATE TABLE IF NOT EXISTS audit_logs (
    id SERIAL PRIMARY KEY,
    action TEXT NOT NULL,
    entity_name TEXT NULL,
    entity_id TEXT NULL,
    controller_name TEXT NULL,
    action_name TEXT NULL,
    http_method TEXT NULL,
    request_path TEXT NULL,
    status_code INTEGER NOT NULL DEFAULT 0,
    succeeded BOOLEAN NOT NULL DEFAULT FALSE,
    user_id INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
    username TEXT NULL,
    user_role TEXT NULL,
    ip_address TEXT NULL,
    user_agent TEXT NULL,
    details_json TEXT NULL,
    created_at_utc TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

CREATE INDEX IF NOT EXISTS ix_audit_logs_user_id
ON audit_logs(user_id);

CREATE INDEX IF NOT EXISTS ix_audit_logs_action
ON audit_logs(action);

CREATE INDEX IF NOT EXISTS ix_audit_logs_created_at_utc
ON audit_logs(created_at_utc DESC);
