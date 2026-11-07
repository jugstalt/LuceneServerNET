-- empty cron = manual job (run now only); catch_up = run once at startup if a scheduled run was missed
ALTER TABLE import_jobs ADD COLUMN catch_up INTEGER NOT NULL DEFAULT 1;
