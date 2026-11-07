-- external trigger (POST /api/jobs/{id}/run): SHA-256 hash of the token; note of the caller per run
ALTER TABLE import_jobs ADD COLUMN trigger_token_hash TEXT NULL;
ALTER TABLE import_job_runs ADD COLUMN note TEXT NULL;
