using FlowSynx.PluginCore;
using FlowSynx.PluginCore.Extensions;
using FlowSynx.PluginCore.Helpers;
using FlowSynx.Plugins.PostgreSql.Models;
using FlowSynx.Plugins.PostgreSql.Services;
using Npgsql;
using NpgsqlTypes;

namespace FlowSynx.Plugins.PostgreSql;

public class PostgreSqlPlugin: IPlugin
{
    private IPluginLogger? _logger;
    private readonly IReflectionGuard _reflectionGuard;
    private PostgreSqlPluginSpecifications _postgreSqlSpecifications = null!;
    private bool _isInitialized;

    public PostgreSqlPlugin()
        : this(new DefaultReflectionGuard()) { }

    internal PostgreSqlPlugin(IReflectionGuard reflectionGuard)
    {
        _reflectionGuard = reflectionGuard ?? throw new ArgumentNullException(nameof(reflectionGuard));
    }

    public PluginMetadata Metadata => new PluginMetadata
    {
        Id = Guid.Parse("e2c349bc-6bfc-4e1e-acce-8dbda585abcf"),
        Name = "PostgreSql",
        CompanyName = "FlowSynx",
        Description = Resources.PluginDescription,
        Version = new Version(1, 1, 0),
        Category = PluginCategory.Database,
        Authors = new List<string> { "FlowSynx" },
        Copyright = "© FlowSynx. All rights reserved.",
        Icon = "flowsynx.png",
        ReadMe = "README.md",
        RepositoryUrl = "https://github.com/flowsynx/plugin-postgresql",
        ProjectUrl = "https://flowsynx.io",
        Tags = new List<string>() { "flowSynx", "sql", "database", "data", "postgresql" },
        MinimumFlowSynxVersion = new Version(1, 1, 1)
    };

    public PluginSpecifications? Specifications { get; set; }

    public Type SpecificationsType => typeof(PostgreSqlPluginSpecifications);

    private Dictionary<string, Func<InputParameter, CancellationToken, Task<object?>>> OperationMap => new(StringComparer.OrdinalIgnoreCase)
    {
        ["query"] = async (parameters, cancellationToken) => await ExecuteQueryAsync(parameters, cancellationToken),
        ["execute"] = async (parameters, cancellationToken) => { await ExecuteNonQueryAsync(parameters, cancellationToken); return null; }
    };

    public IReadOnlyCollection<string> SupportedOperations => OperationMap.Keys;

    public Task Initialize(IPluginLogger logger)
    {
        ThrowIfReflection();
        ArgumentNullException.ThrowIfNull(logger);
        _postgreSqlSpecifications = Specifications.ToObject<PostgreSqlPluginSpecifications>();
        _logger = logger;
        _isInitialized = true;
        return Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginParameters parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfReflection();
        ThrowIfNotInitialized();

        var inputParameter = parameters.ToObject<InputParameter>();
        var operation = inputParameter.Operation;

        if (OperationMap.TryGetValue(operation, out var handler))
        {
            return await handler(inputParameter, cancellationToken);
        }

        throw new NotSupportedException($"PostgreSQL plugin: Operation '{operation}' is not supported.");
    }

    private void ThrowIfReflection()
    {
        if (_reflectionGuard.IsCalledViaReflection())
            throw new InvalidOperationException(Resources.ReflectionBasedAccessIsNotAllowed);
    }

    private void ThrowIfNotInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException($"Plugin '{Metadata.Name}' v{Metadata.Version} is not initialized.");
    }

    private async Task ExecuteNonQueryAsync(InputParameter parameters, CancellationToken cancellationToken)
    {
        var (sql, sqlParams) = GetSqlAndParameters(parameters);

        try
        {
            var connectionString = NormalizePostgresConnectionString(_postgreSqlSpecifications.ConnectionString);
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = new NpgsqlCommand(parameters.Sql, connection);

            AddParameters(cmd, sqlParams);

            cancellationToken.ThrowIfCancellationRequested();

            int affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger?.LogInfo($"Non-query executed successfully. Rows affected: {affectedRows}.");
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error executing PostgreSQL sql statement. Error: {ex.Message}");
            throw;
        }
    }

    private async Task<PluginContext> ExecuteQueryAsync(InputParameter parameters, CancellationToken cancellationToken)
    {
        var (sql, sqlParams) = GetSqlAndParameters(parameters);

        try
        {
            var result = new List<Dictionary<string, object>>();
            var connectionString = NormalizePostgresConnectionString(_postgreSqlSpecifications.ConnectionString);
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var cmd = new NpgsqlCommand(sql, connection);

            AddParameters(cmd, sqlParams);

            cancellationToken.ThrowIfCancellationRequested();

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                result.Add(row);
            }

            _logger?.LogInfo($"Query executed successfully. Rows returned: {result.Count}.");
            string key = $"{Guid.NewGuid().ToString()}";
            return new PluginContext(key, "Data")
            {
                Format = "Database",
                StructuredData = result
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error executing PostgreSQL sql statement. Error: {ex.Message}");
            throw;
        }
    }

    private (string Sql, Dictionary<string, object> Parameters) GetSqlAndParameters(InputParameter parameters)
    {
        if (string.IsNullOrEmpty(parameters.Sql))
            throw new ArgumentException("Missing 'sql' parameter.");
        
        Dictionary<string, object> sqlParams = new();

        if (parameters.Params is Dictionary<string, object> paramDict)
        {
            sqlParams = paramDict;
        }

        return (parameters.Sql, sqlParams);
    }

    private void AddParameters(NpgsqlCommand cmd, Dictionary<string, object>? parameters)
    {
        if (parameters == null) return;

        foreach (var kvp in parameters)
        {
            var paramName = kvp.Key;
            var paramValue = kvp.Value;

            if (paramValue == null)
            {
                cmd.Parameters.AddWithValue(paramName, DBNull.Value);
            }
            else if (paramValue is Guid guidValue)
            {
                cmd.Parameters.Add(paramName, NpgsqlDbType.Uuid).Value = guidValue;
            }
            else if (paramValue is string strValue)
            {
                // Try to detect if string is Guid
                if (Guid.TryParse(strValue, out var parsedGuid))
                {
                    cmd.Parameters.Add(paramName, NpgsqlDbType.Uuid).Value = parsedGuid;
                }
                else
                {
                    cmd.Parameters.AddWithValue(paramName, strValue);
                }
            }
            else if (paramValue is int || paramValue is long || paramValue is double || paramValue is decimal)
            {
                cmd.Parameters.AddWithValue(paramName, paramValue);
            }
            else if (paramValue is bool boolValue)
            {
                cmd.Parameters.AddWithValue(paramName, boolValue);
            }
            else if (paramValue is DateTime dateValue)
            {
                cmd.Parameters.AddWithValue(paramName, NpgsqlDbType.Timestamp).Value = dateValue;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported parameter type for '{paramName}': {paramValue.GetType()}");
            }
        }
    }

    private string NormalizePostgresConnectionString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new Exception("Database connectionstring is required!");

        if (input.StartsWith("postgres://") || input.StartsWith("postgresql://"))
        {
            var uri = new Uri(input);
            var userInfo = uri.UserInfo.Split(':', 2);
            var username = userInfo[0];
            var password = userInfo.Length > 1 ? userInfo[1] : "";
            var host = uri.Host;
            var port = uri.Port == -1 ? 5432 : uri.Port;

            return new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Username = username,
                Password = password,
                Database = uri.AbsolutePath.TrimStart('/'),
                SslMode = SslMode.Require
            }.ConnectionString;
        }

        return input;
    }
}