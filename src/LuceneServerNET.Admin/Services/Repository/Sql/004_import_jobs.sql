CREATE TABLE import_jobs (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    name             TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    instance         TEXT    NOT NULL,
    connection_id    INTEGER NOT NULL,
    sql              TEXT    NOT NULL,
    target           TEXT    NOT NULL,
    mode             TEXT    NOT NULL,
    mapping_json     TEXT    NOT NULL,
    batch_size       INTEGER NOT NULL,
    max_errors       INTEGER NOT NULL,
    keep_generations INTEGER NOT NULL,
    cron             TEXT    NOT NULL,
    time_zone        TEXT    NULL,
    enabled          INTEGER NOT NULL,
    created_utc      TEXT    NOT NULL,
    modified_utc     TEXT    NOT NULL
);

CREATE TABLE import_job_runs (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    job_id       INTEGER NOT NULL REFERENCES import_jobs(id) ON DELETE CASCADE,
    started_utc  TEXT    NOT NULL,
    finished_utc TEXT    NULL,
    status       TEXT    NOT NULL,
    triggered_by TEXT    NOT NULL,
    index_name   TEXT    NULL,
    read_count   INTEGER NOT NULL DEFAULT 0,
    imported     INTEGER NOT NULL DEFAULT 0,
    failed       INTEGER NOT NULL DEFAULT 0,
    message      TEXT    NULL
);

CREATE INDEX ix_import_job_runs_job ON import_job_runs (job_id, started_utc);
