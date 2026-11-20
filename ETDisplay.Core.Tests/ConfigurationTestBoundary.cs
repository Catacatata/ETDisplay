// Test-only boundary for the linked production Configuration.cs. These types are
// compiled only by ETDisplay.Core.Tests, never referenced by the plugin project.
namespace Dalamud.Configuration
{
    // Only the configuration model's actual Dalamud contract is reproduced here.
    public interface IPluginConfiguration
    {
        int Version { get; set; }
    }
}

namespace ETDisplay
{
    internal static class Services
    {
        internal static ConfigurationPluginInterfaceStub PluginInterface { get; } = new();
    }

    internal sealed class ConfigurationPluginInterfaceStub
    {
        internal Configuration? LastSavedConfiguration { get; private set; }
        internal int SaveCallCount { get; private set; }

        public void SavePluginConfig(Configuration configuration)
        {
            LastSavedConfiguration = configuration;
            SaveCallCount++;
        }

        internal void Reset()
        {
            LastSavedConfiguration = null;
            SaveCallCount = 0;
        }
    }
}

namespace ETDisplay.Core.Tests
{
    // The capture is static, matching Services. Isolate this collection from all
    // other collections, and reset before/after every test (including failures).
    [Xunit.CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ConfigurationTestCollection
    {
        public const string Name = "Configuration static service boundary";
    }
}
