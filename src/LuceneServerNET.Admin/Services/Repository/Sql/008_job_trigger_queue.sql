-- triggers with parameters (append or update): collected per job, processed by JobQueueProcessor
CREATE TABLE job_trigger_queue (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    job_id       INTEGER NOT NULL REFERENCES import_jobs(id) ON DELETE CASCADE,
    kind         TEXT    NOT NULL,   -- params | delete
    source       TEXT    NULL,       -- source name, null = all matching sources
    params_json  TEXT    NOT NULL,
    note         TEXT    NULL,
    received_utc TEXT    NOT NULL
);

CREATE INDEX ix_job_trigger_queue_job ON job_trigger_queue (job_id, id);
