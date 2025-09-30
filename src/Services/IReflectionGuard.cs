namespace FlowSynx.Plugins.PostgreSql.Services;

public interface IReflectionGuard
{
    bool IsCalledViaReflection();
}