---
name: test-database
description: Create, query and drop test databases on the local SQL Server test instance (Docker, localhost,1433) as login `claude`. Use when you need real metadata or data to check the metadata loader, completion scenarios, JOIN suggestions, linked servers or integration tests, or when the user asks for test data.
---

# Local test database

The user runs a SQL Server test instance in Docker on `localhost,1433` (container `mssql`). It is a throwaway test server: you may create, change and drop your own databases freely. It is never a production server.

## Connection

- Login `claude`. The password and connection options are in `%LOCALAPPDATA%\SqlPrompter\testdb.json`, outside the repository. Never copy the password into the repository, a commit, an issue or a pull request.
- The login is a member of `dbcreator` (owns the databases it creates) and `setupadmin` (linked servers), and has `VIEW ANY DEFINITION` and `VIEW SERVER STATE`. It cannot read data in other databases.
- If the config file is missing or the login fails, tell the user. The login is created by a script they run as `sa`; do not try to work around it.
- The test server is only available locally on the user's Windows machine, not in cloud sessions.

## Running SQL

Use the helper script with PowerShell 7 from the repository root:

```powershell
./.claude/skills/test-database/invoke-sql.ps1 "SELECT name FROM sys.databases"
./.claude/skills/test-database/invoke-sql.ps1 -Database SqlPrompterTest_Main "SELECT * FROM sys.tables"
./.claude/skills/test-database/invoke-sql.ps1 -File tests/TestDatabase/schema.sql -Database SqlPrompterTest_Main
```

- Batches are split on `GO` lines, like `sqlcmd`.
- Result sets are printed as tables (at most `-MaxRows` rows, default 200). `PRINT` and info messages are printed with a `--` prefix.
- The script exits with code 1 when a batch fails and prints the error.

## Conventions

- Name every database you create `SqlPrompterTest_<Purpose>`, e.g. `SqlPrompterTest_Joins`. Touch no other databases.
- Make scripts idempotent: drop and recreate the database, or use `IF NOT EXISTS`.
- Scripts that tests or reproductions depend on go into `tests/TestDatabase/` in the repository, in English, so the database can be recreated anywhere. Throwaway exploration scripts go into the scratchpad.
- Model test schemas on realistic conventions (e.g. `IdTable` primary keys, foreign keys named after the referenced table, several schemas, views, procedures, triggers, table-valued functions), but never on the user's real schemas or data.
- When you are done with a throwaway database, drop it.
