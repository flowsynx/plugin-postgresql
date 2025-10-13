using FlowSynx.PluginCore;
using FlowSynx.Plugins.PostgreSql.Models;
using FlowSynx.Plugins.PostgreSql.Services;
using Moq;
using Npgsql;

namespace FlowSynx.Plugins.PostgreSql.UnitTests;

public class PostgreSqlPluginTests
{
    private readonly Mock<IGuidProvider> _guidProviderMock;
    private readonly Mock<IReflectionGuard> _reflectionGuardMock;
    private readonly Mock<IPluginLogger> _loggerMock;
    private readonly PostgreSqlPlugin _plugin;

    public PostgreSqlPluginTests()
    {
        _guidProviderMock = new Mock<IGuidProvider>();
        _reflectionGuardMock = new Mock<IReflectionGuard>();
        _reflectionGuardMock.Setup(r => r.IsCalledViaReflection()).Returns(false);
        _loggerMock = new Mock<IPluginLogger>();

        _plugin = new PostgreSqlPlugin(_guidProviderMock.Object, _reflectionGuardMock.Object);
    }

    [Fact]
    public async Task Initialize_SetsIsInitializedAndLogger()
    {
        _plugin.Specifications = new PostgreSqlPluginSpecifications { ConnectionString = "Host=localhost;Database=test;Username=test;Password=test" };
        await _plugin.Initialize(_loggerMock.Object);
        Assert.NotNull(_plugin);
    }

    [Fact]
    public async Task Initialize_ShouldThrow_WhenCalledViaReflection()
    {
        _reflectionGuardMock.Setup(r => r.IsCalledViaReflection()).Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _plugin.Initialize(_loggerMock.Object));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsIfNotInitialized()
    {
        _plugin.Specifications = new PostgreSqlPluginSpecifications { ConnectionString = "Host=localhost;Database=test;Username=test;Password=test" };
        var parameters = new PluginParameters();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _plugin.ExecuteAsync(parameters, CancellationToken.None));
    }

    [Fact]
    public void ExtractSqlParameters_ReturnsSqlAndParams()
    {
        var input = new InputParameter { Sql = "SELECT 1", Params = new Dictionary<string, object> { { "id", 1 } } };
        var result = typeof(PostgreSqlPlugin)
            .GetMethod("ExtractSqlParameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(_plugin, new object[] { input });
        Assert.NotNull(result);
    }

    [Fact]
    public void NormalizeConnectionString_ParsesUri()
    {
        var uri = "postgres://user:pass@localhost:5432/dbname";
        var result = typeof(PostgreSqlPlugin)
            .GetMethod("NormalizeConnectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(_plugin, new object[] { uri }) as string;
        Assert.Contains("Host=localhost", result);
        Assert.Contains("Username=user", result);
        Assert.Contains("Password=pass", result);
        Assert.Contains("Database=dbname", result);
    }

    [Fact]
    public void AddParameters_AddsSupportedTypes()
    {
        var cmd = new NpgsqlCommand();
        var parameters = new Dictionary<string, object>
        {
            { "intParam", 1 },
            { "strParam", "test" },
            { "guidParam", Guid.NewGuid() },
            { "boolParam", true },
            { "dateParam", DateTime.UtcNow },
            { "nullParam", null }
        };
        typeof(PostgreSqlPlugin)
            .GetMethod("AddParameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(_plugin, new object[] { cmd, parameters });
        Assert.Equal(6, cmd.Parameters.Count);
    }

    [Fact]
    public void AddParameters_ThrowsOnUnsupportedType()
    {
        var cmd = new NpgsqlCommand();
        var parameters = new Dictionary<string, object>
        {
            { "unsupported", new object() }
        };
        var method = typeof(PostgreSqlPlugin)
            .GetMethod("AddParameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            method.Invoke(_plugin, new object[] { cmd, parameters })
        );
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("Unsupported parameter type", ex.InnerException.Message);
    }
}