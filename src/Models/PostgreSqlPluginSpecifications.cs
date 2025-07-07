using FlowSynx.PluginCore;

namespace FlowSynx.Plugins.PostgreSql.Models;

public class PostgreSqlPluginSpecifications: PluginSpecifications
{
    [RequiredMember]
    public string ConnectionString { get; set; } = string.Empty;
}