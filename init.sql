DROP TABLE IF EXISTS druginfo;
CREATE TABLE druginfo (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    manu_lotnum TEXT,
    manu_date TEXT,
    expy_end TEXT,
    kcsb INTEGER DEFAULT 0,
    msg TEXT,
    create_time DATETIME DEFAULT CURRENT_TIMESTAMP
);
