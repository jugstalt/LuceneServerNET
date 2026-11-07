CREATE TABLE instances (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    name                    TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    url                     TEXT    NOT NULL,
    client_id               TEXT    NULL,
    client_secret_protected TEXT    NULL,
    created_utc             TEXT    NOT NULL,
    modified_utc            TEXT    NOT NULL
);
