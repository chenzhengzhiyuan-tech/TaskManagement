"""Read-only SQLite comparison. Prints counts/hashes, never business contents or secrets."""
import hashlib
import json
import sqlite3
import sys
from pathlib import Path

def connect(path):
    return sqlite3.connect(Path(path).resolve().as_uri() + '?mode=ro', uri=True)

def quoted(value):
    return '"' + value.replace('"', '""') + '"'

with connect(sys.argv[1]) as before, connect(sys.argv[2]) as after:
    tables = [row[0] for row in before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
    failed = []
    for table in sorted(tables):
        if table in ('AuthSessions', 'JobLocks', 'NotificationLogs', '__EFMigrationsHistory'):
            continue  # Runtime housekeeping may legitimately change these tables.
        columns = [row[1] for row in before.execute('PRAGMA table_info(' + quoted(table) + ')')]
        select = 'SELECT ' + ','.join(map(quoted, columns)) + ' FROM ' + quoted(table)
        old = sorted((json.dumps(row, ensure_ascii=True, default=str) for row in before.execute(select)))
        new = sorted((json.dumps(row, ensure_ascii=True, default=str) for row in after.execute(select)))
        equal = old == new
        print(json.dumps({'table': table, 'before': len(old), 'after': len(new), 'unchanged': equal, 'before_sha256': hashlib.sha256('\n'.join(old).encode()).hexdigest(), 'after_sha256': hashlib.sha256('\n'.join(new).encode()).hexdigest()}))
        if not equal:
            failed.append(table)
    sys.exit(1 if failed else 0)
