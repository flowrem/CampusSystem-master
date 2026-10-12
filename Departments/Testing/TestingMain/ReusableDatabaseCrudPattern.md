> **Historical document.** This was written for the earlier SQL design (the MySQL table browser the Testing app used to be. Testing is now a JSON collection browser; see Testing-Database-Tables.md). The project now runs on one shared JSON datastore with no SQL, EF Core or MySQL. Table, schema and connection details below are out of date. For the current design read `README.md` and `Shared/CampusSystem.Data/Json/README.md`.

# Reusable Database CRUD Pattern for ASP.NET Department Apps

This document explains how the Testing department implements a simple, reusable database interaction pattern that can be copied to other ASP.NET department applications in this campus system.

## Goal

The goal is to let a departmental app do the following without creating a large framework:

1. Submit data to a remote MariaDB database
2. Choose a database and table from the UI
3. View table contents
4. Edit rows
5. Delete rows
6. Keep the pattern safe, predictable, and easy to copy to another app

## Core ideas

### 1. Use a single page as a testing and reference UI

The page at `Pages/Index.cshtml` acts as a reference implementation. It keeps:

- form submission for insert
- database chooser
- table chooser
- table viewer
- row edit/delete actions

This gives a department an easy place to learn and copy the pattern before moving it into a proper feature page.

### 2. Keep database access in the Razor Page model

For a small department app, the simplest pattern is:

- page model handles `GET` and `POST`
- database calls use `MySqlConnection` and `MySqlCommand`
- `BindProperty` binds values from HTML inputs
- results are rendered directly into the page

This pattern is easier for other departments to understand than introducing a full repository layer too early.

### 3. Prefer parameterized queries

Always use parameterized queries for values like:

- `name`
- `email`
- `id`
- any user input

Example:

```csharp
await using var command = new MySqlCommand(sql, connection);
command.Parameters.AddWithValue("@name", Name);
command.Parameters.AddWithValue("@email", Email);
```

This prevents SQL injection and is the standard ASP.NET + MySQL pattern.

### 4. Use a connection string in `appsettings.json`

Each department should keep its own `DefaultConnection` or department-specific connection string in `appsettings.json`.

Example:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=100.98.41.69;Port=3306;Database=mydb;Uid=myuser;Pwd=strongpassword;"
}
```

This keeps environment-specific configuration out of code and easy to replace per deployment.

### 5. Use table discovery queries for generic display

For a generic viewer, use:

```sql
SHOW DATABASES;
SHOW TABLES FROM `database_name`;
SELECT * FROM `database_name`.`table_name`;
```

This lets the UI discover what is available without hardcoding all table names.

### 6. Detect the primary key automatically

For edit and delete actions, use the schema metadata to find a row identifier:

```sql
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table AND COLUMN_KEY = 'PRI'
LIMIT 1;
```

This is important because row updates and deletes should use the primary key instead of assuming a column name like `id` exists.

### 7. Confirm destructive actions in the browser

Use `confirm()` in the HTML before deleting a row:

```html
<button type="submit" onclick="return confirm('Are you sure you want to delete this row?');">
    Delete
</button>
```

This protects users from accidental data removal.

## Minimal reusable pattern

### Step 1: Add MySqlConnector package

```powershell
dotnet add package MySqlConnector
```

### Step 2: Add a connection string

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=100.98.41.69;Port=3306;Database=mydb;Uid=myuser;Pwd=strongpassword;"
}
```

### Step 3: Use a page model with:

- `BindProperty` for form fields
- a `GET` method to load options and rows
- `POST` methods for insert, update, delete
- helper methods to fetch database and table lists
- helper method to detect the primary key

### Step 4: Use a UI with:

- insert form
- database drop-down
- table drop-down
- table viewer
- row action buttons with confirmation prompts

## Example insert pattern

```csharp
const string sql = "INSERT INTO users (name, email) VALUES (@name, @email);";
await using var command = new MySqlCommand(sql, connection);
command.Parameters.AddWithValue("@name", Name);
command.Parameters.AddWithValue("@email", Email);
await command.ExecuteNonQueryAsync();
```

## Example table load pattern

```csharp
var query = $"SELECT * FROM `{dbName}`.`{tableName}`;";
await using var command = new MySqlCommand(query, connection);
await using var reader = await command.ExecuteReaderAsync();
```

## Example delete pattern

```csharp
var sql = $"DELETE FROM `{db}`.`{table}` WHERE `{idColumn}` = @id;";
await using var command = new MySqlCommand(sql, connection);
command.Parameters.AddWithValue("@id", rowId);
await command.ExecuteNonQueryAsync();
```

## Example update pattern

```csharp
var sql = $"UPDATE `{db}`.`{table}` SET `name` = @p_name WHERE `id` = @rowId;";
```

## Good departments to apply this to

This pattern is useful for any department that needs simple record handling, including:

- Faculty Portal
- Registrar
- Finance
- Library
- Guidance Department
- Student Portal

## Important caution

This pattern is intentionally simple and works well for internal CRUD testing. Before moving to production, add:

- role-based authorization
- validation rules
- proper password hashing
- audit logging
- transaction handling
- consistency checks
- input sanitization beyond simple form validation

## Reuse instructions for other departments

To copy this pattern:

1. Create a new Razor Page or replace the department home page.
2. Keep the same `MySqlConnector` package reference.
3. Update the connection string and table names.
4. Replace the insert fields with the real department data model.
5. Keep the same `SHOW DATABASES` / `SHOW TABLES` / `SELECT *` pattern for viewing.
6. Keep the `confirm()` delete action.
7. Add a custom `id` detection helper for each table.

This keeps the implementation generic enough to be copied across all department apps without changing the overall architecture.
