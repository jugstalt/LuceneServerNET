CREATE TABLE db_connections (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    name                        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    provider                    TEXT    NOT NULL,
    connection_string_protected TEXT    NOT NULL,
    created_utc                 TEXT    NOT NULL,
    modified_utc                TEXT    NOT NULL
);
