-- webhook called after a run (always or only on failure); header "Name: value" encrypted with DataProtection
ALTER TABLE import_jobs ADD COLUMN webhook_url TEXT NULL;
ALTER TABLE import_jobs ADD COLUMN webhook_on TEXT NOT NULL DEFAULT 'failure';
ALTER TABLE import_jobs ADD COLUMN webhook_header_protected TEXT NULL;
ALTER TABLE import_job_runs ADD COLUMN webhook_result TEXT NULL;
