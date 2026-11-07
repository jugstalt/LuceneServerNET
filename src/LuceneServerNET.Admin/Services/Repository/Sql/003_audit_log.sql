CREATE TABLE audit_log (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc TEXT    NOT NULL,
    user_name     TEXT    NOT NULL,
    action        TEXT    NOT NULL,
    instance      TEXT    NULL,
    target        TEXT    NULL,
    details       TEXT    NULL,
    success       INTEGER NOT NULL
);

CREATE INDEX ix_audit_log_timestamp ON audit_log (timestamp_utc);
