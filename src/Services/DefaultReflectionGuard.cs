using FlowSynx.PluginCore.Helpers;

namespace FlowSynx.Plugins.PostgreSql.Services;

internal class DefaultReflectionGuard : IReflectionGuard
{
    public bool IsCalledViaReflection() => ReflectionHelper.IsCalledViaReflection();
}