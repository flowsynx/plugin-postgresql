using FlowSynx.PluginCore;
using FlowSynx.PluginCore.Extensions;
using FlowSynx.Plugins.PostgreSql.Models;
using FlowSynx.Plugins.PostgreSql.Services;
using Npgsql;
using NpgsqlTypes;

namespace FlowSynx.Plugins.PostgreSql;

public class PostgreSqlPlugin : IPlugin
{
    private readonly IGuidProvider _guidProvider;
    private readonly IReflectionGuard _reflectionGuard;
    private IPluginLogger? _logger;
    private PostgreSqlPluginSpecifications _specifications = null!;
    private bool _isInitialized;

    public PostgreSqlPlugin()
        : this(new GuidProvider(), new DefaultReflectionGuard()) { }

    internal PostgreSqlPlugin(IGuidProvider guidProvider, IReflectionGuard reflectionGuard)
    {
        _guidProvider = guidProvider ?? throw new ArgumentNullException(nameof(guidProvider));
        _reflectionGuard = reflectionGuard ?? throw new ArgumentNullException(nameof(reflectionGuard));
    }

    public PluginMetadata Metadata => new()
    {
        Id = Guid.Parse("e2c349bc-6bfc-4e1e-acce-8dbda585abcf"),
        Name = "PostgreSql",
        CompanyName = "FlowSynx",
        Description = Resources.PluginDescription,
        Version = new Version(1, 2, 0),
        Category = PluginCategory.Database,
        Authors = new List<string> { "FlowSynx" },
        Copyright = "© FlowSynx. All rights reserved.",
        Icon = "flowsynx.png",
        ReadMe = "README.md",
        RepositoryUrl = "https://github.com/flowsynx/plugin-postgresql",
        ProjectUrl = "https://flowsynx.io",
        Tags = new List<string> { "flowSynx", "sql", "database", "data", "postgresql" },
        MinimumFlowSynxVersion = new Version(1, 1, 1)
    };

    public PluginSpecifications? Specifications { get; set; }
    public Type SpecificationsType => typeof(PostgreSqlPluginSpecifications);

    private Dictionary<string, Func<InputParameter, CancellationToken, Task<object?>>> OperationMap =>
        new Dictionary<string, Func<InputParameter, CancellationToken, Task<object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = async (p, t) => await ExecuteQueryAsync(p, t),
            ["execute"] = async (p, t) => { await ExecuteNonQueryAsync(p, t); return null; }
        };

    public IReadOnlyCollection<string> SupportedOperations => OperationMap.Keys;

    public Task Initialize(IPluginLogger logger)
    {
        ThrowIfReflection();
        ArgumentNullException.ThrowIfNull(logger);

        _specifications = Specifications.ToObject<PostgreSqlPluginSpecifications>();
        _logger = logger;
        _isInitialized = true;

        return Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginParameters parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfReflection();
        ThrowIfNotInitialized();

        var input = parameters.ToObject<InputParameter>();
        if (!OperationMap.TryGetValue(input.Operation, out var handler))
            throw new NotSupportedException($"PostgreSQL plugin: Operation '{input.Operation}' is not supported.");

        return await handler(input, cancellationToken);
    }

    #region private methods

    private async Task ExecuteNonQueryAsync(InputParameter input, CancellationToken token)
    {
        var (sql, sqlParams) = ExtractSqlParameters(input);
        var connectionString = NormalizeConnectionString(_specifications.ConnectionString);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token);

            var context = ParseInputData(input.Data);

            if (context.StructuredData?.Count > 0)
            {
                await ExecuteStructuredDataAsync(connection, sql, context, token);
            }
            else
            {
                await ExecuteSingleNonQueryAsync(connection, sql, sqlParams, token);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error executing PostgreSQL SQL statement: {ex.Message}");
            throw;
        }
    }

    private async Task<PluginContext> ExecuteQueryAsync(InputParameter input, CancellationToken token)
    {
        var (sql, sqlParams) = ExtractSqlParameters(input);
        var connectionString = NormalizeConnectionString(_specifications.ConnectionString);

        try
        {
            var result = new List<Dictionary<string, object>>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token);

            await using var cmd = new NpgsqlCommand(sql, connection);
            AddParameters(cmd, sqlParams);

            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var row = Enumerable.Range(0, reader.FieldCount)
                                    .ToDictionary(reader.GetName, reader.GetValue);
                result.Add(row);
            }

            _logger?.LogInfo($"Query executed successfully. Rows returned: {result.Count}.");

            return new PluginContext(_guidProvider.NewGuid().ToString(), "Data")
            {
                Format = "Database",
                StructuredData = result
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error executing PostgreSQL query: {ex.Message}");
            throw;
        }
    }

    private async Task ExecuteStructuredDataAsync(
        NpgsqlConnection connection, string sql, PluginContext context, CancellationToken token)
    {
        int totalAffected = 0;

        if (context.StructuredData is not null)
        {
            foreach (var row in context.StructuredData)
            {
                await using var cmd = new NpgsqlCommand(sql, connection);
                AddParameters(cmd, row);
                totalAffected += await cmd.ExecuteNonQueryAsync(token);
            }
        }

        _logger?.LogInfo($"Executed structured SQL for {context.StructuredData?.Count} rows. Total affected: {totalAffected}");
    }

    private async Task ExecuteSingleNonQueryAsync(
        NpgsqlConnection connection, string sql, Dictionary<string, object> sqlParams, CancellationToken token)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        AddParameters(cmd, sqlParams);
        int affected = await cmd.ExecuteNonQueryAsync(token);
        _logger?.LogInfo($"Non-query executed successfully. Rows affected: {affected}.");
    }

    private (string Sql, Dictionary<string, object> Params) ExtractSqlParameters(InputParameter input)
    {
        if (string.IsNullOrWhiteSpace(input.Sql))
            throw new ArgumentException("Missing 'sql' parameter.");

        var sqlParams = input.Params as Dictionary<string, object> ?? new();
        return (input.Sql, sqlParams);
    }

    private void AddParameters(NpgsqlCommand cmd, Dictionary<string, object>? parameters)
    {
        if (parameters is null) return;

        foreach (var (key, value) in parameters)
        {
            var name = key.StartsWith("@") ? key : "@" + key;

            cmd.Parameters.Add(value switch
            {
                null => new NpgsqlParameter(name, DBNull.Value),
                Guid g => new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = g },
                string s when Guid.TryParse(s, out var parsed) => new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = parsed },
                string s => new NpgsqlParameter(name, s),
                int or long or double or decimal or bool => new NpgsqlParameter(name, value),
                DateTime dt => new NpgsqlParameter(name, NpgsqlDbType.Timestamp) { Value = dt },
                _ => throw new InvalidOperationException($"Unsupported parameter type for '{name}': {value.GetType()}")
            });
        }
    }

    private string NormalizeConnectionString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("Database connection string is required.");

        if (!input.StartsWith("postgres://") && !input.StartsWith("postgresql://"))
            return input;

        var uri = new Uri(input);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port == -1 ? 5432 : uri.Port,
            Username = username,
            Password = password,
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require
        }.ConnectionString;
    }

    private PluginContext ParseInputData(object? data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data), "Input data cannot be null.");

        return data switch
        {
            PluginContext context => context,
            IEnumerable<PluginContext> => throw new NotSupportedException("List of PluginContext is not supported."),
            _ => throw new NotSupportedException("Unsupported input data format.")
        };
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

    #endregion
}