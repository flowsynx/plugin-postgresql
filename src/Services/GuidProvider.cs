namespace FlowSynx.Plugins.PostgreSql.Services;

internal class GuidProvider : IGuidProvider
{
    public Guid NewGuid() => Guid.NewGuid();
}