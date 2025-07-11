# FlowSynx PostgreSQL Plugin

The PostgreSQL Plugin is a pre-packaged, plug-and-play integration component for the FlowSynx engine. It enables executing PostgreSQL queries with configurable parameters such as connection strings, SQL templates, and runtime parameters. Designed for FlowSynx’s no-code/low-code automation workflows, this plugin simplifies database integration, data retrieval, and transformation tasks.

This plugin is automatically installed by the FlowSynx engine when selected within the platform. It is not intended for manual installation or standalone developer use outside the FlowSynx environment.

---

## Purpose

The PostgreSQL Plugin allows FlowSynx users to:

- Execute parameterized SQL commands securely.
- Retrieve data from PostgreSQL and pass it downstream in workflows.
- Perform data transformation and filtering inline using SQL.
- Integrate PostgreSQL operations into automation workflows without writing code.

---

## Supported Operations

- **query**: Executes a SQL SELECT query and returns the result set as JSON.
- **execute**: Executes a SQL command (INSERT, UPDATE, DELETE, etc.) and returns the number of affected rows.

---

## Plugin Specifications

The plugin requires the following configuration:
- ConnectionString (string): **Required.** The PostgreSQL connection string used to connect to the database. Example:
```
Host=localhost;Port=5432;Username=postgres;Password=secret;Database=mydb
```

---

## Input Parameters

The plugin accepts the following parameters:

- `Operation` (string): **Required.** The type of operation to perform. Supported values are query and execute.  
- `Sql ` (string): **Required.** The SQL query or command to execute. Use parameter placeholders (e.g., @id, @name) for dynamic values.  
- `Params` (object): Optional. A dictionary of parameter names and values to be used in the SQL template.

### Example input

```json
{
  "Operation": "query",
  "Sql": "SELECT id, name, email FROM users WHERE country = @country",
  "Parameters": {
    "country": "Norway"
  }
}
```

---

## Debugging Tips

- Ensure the ConnectionString is correct and the database is accessible from the FlowSynx environment.
- Use parameter placeholders (@parameterName) in the SQL to prevent SQL injection and enable parameterization.
- Validate that all required parameters are provided in the Parameters dictionary.
- If a query returns no results, verify that your SQL WHERE conditions are correct and the target table contains matching data. 

---

## Security Notes

- SQL commands are executed using parameterized queries to prevent SQL injection.
- The plugin does not store credentials or data outside of execution unless explicitly configured.
- Only authorized FlowSynx platform users can view or modify configurations.

---

## License

© FlowSynx. All rights reserved.