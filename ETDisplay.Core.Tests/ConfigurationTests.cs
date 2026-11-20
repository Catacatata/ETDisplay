using Dalamud.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ETDisplay.Core.Tests;

// Model/Json.NET regression tests, NOT an integration test of Dalamud's actual
// config-file loading, serializer settings, file writes, or backup behavior.
[Collection(ConfigurationTestCollection.Name)]
public sealed class ConfigurationTests : IDisposable
{
    public ConfigurationTests() => Services.PluginInterface.Reset();

    public void Dispose() => Services.PluginInterface.Reset();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MissingFieldsUseModelDefaultsAndOldVersionsUpgrade(int version)
    {
        var configuration = Deserialize($"{{\"Version\":{version}}}");
        Assert.Equal(version, configuration.Version);
        configuration.Validate();
        AssertDefaults(configuration);
    }

    [Fact]
    public void MissingVersionUsesCurrentModelDefault()
    {
        var configuration = Deserialize("{}");
        AssertDefaults(configuration);
        configuration.Validate();
        AssertDefaults(configuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MigrationPreservesExplicitFalseColorsVolumeLockAndCalibration(int version)
    {
        var expected = PreferenceJson(version);
        var configuration = Deserialize(expected.ToString(Formatting.None));
        configuration.Validate();
        expected[nameof(Configuration.Version)] = 4;
        Assert.True(JToken.DeepEquals(expected, JObject.FromObject(configuration)),
            "Validation must preserve every explicit preference, including false switches and unlocked state.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void FutureVersionIsNeverDowngraded(int version)
    {
        var configuration = Deserialize($"{{\"Version\":{version},\"FontSize\":-1}}");
        configuration.Validate();
        Assert.Equal(version, configuration.Version);
        Assert.Equal(20f, configuration.FontSize); // future version does not bypass safety checks
        Assert.Equal(version, Deserialize(JsonConvert.SerializeObject(configuration)).Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyMemoryFieldsAreIgnoredAndNotWrittenBack(int version)
    {
        var json = new JObject
        {
            ["Version"] = version,
            ["ServerTimeMemoryAddress"] = "0xDEADBEEF",
            ["EnableMemoryTime"] = true,
        };
        var configuration = Deserialize(json.ToString(Formatting.None));
        configuration.Validate();
        AssertDefaults(configuration);
        var serialized = JObject.Parse(JsonConvert.SerializeObject(configuration));
        Assert.Null(serialized.Property("ServerTimeMemoryAddress"));
        Assert.Null(serialized.Property("EnableMemoryTime"));
        Assert.Equal(17, serialized.Properties().Count());
    }

    public static IEnumerable<object[]> FloatValidationCases()
    {
        foreach (var (property, minimum, maximum, fallback) in new[]
        {
            (nameof(Configuration.FontSize), 20f, 96f, 48f),
            (nameof(Configuration.OutlineThickness), 0f, 4f, 2f),
            (nameof(Configuration.SSAlertDuration), 1f, 30f, 5f),
            (nameof(Configuration.TimeCalibrationOffset), -20f, 20f, 0f),
        })
        {
            yield return new object[] { property, float.NaN, fallback };
            yield return new object[] { property, float.NegativeInfinity, fallback };
            yield return new object[] { property, float.PositiveInfinity, fallback };
            yield return new object[] { property, float.MinValue, minimum };
            yield return new object[] { property, float.MaxValue, maximum };
            yield return new object[] { property, minimum - 1, minimum };
            yield return new object[] { property, minimum, minimum };
            yield return new object[] { property, maximum, maximum };
            yield return new object[] { property, maximum + 1, maximum };
            yield return new object[] { property, (minimum + maximum) / 2, (minimum + maximum) / 2 };
        }
    }

    [Theory]
    [MemberData(nameof(FloatValidationCases))]
    public void FloatValidationHandlesNonFiniteBoundsAndValidValues(string property, float input, float expected)
    {
        // Json.NET represents non-finite float values as quoted strings by default.
        // Exercise that real JSON path as well as the actual model's Validate().
        var json = new JObject { [property] = JToken.FromObject(input) };
        var configuration = Deserialize(json.ToString(Formatting.None));
        var propertyInfo = typeof(Configuration).GetProperty(property)!;
        var actualInput = Assert.IsType<float>(propertyInfo.GetValue(configuration));
        if (float.IsNaN(input))
            Assert.True(float.IsNaN(actualInput));
        else
            Assert.Equal(input, actualInput);

        configuration.Validate();
        Assert.Equal(expected, Assert.IsType<float>(propertyInfo.GetValue(configuration)));
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(37, 37)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public void SpeechVolumeClampsToInclusiveIntegerRange(int input, int expected)
    {
        // Volume is an int contract: non-finite values apply to the four float
        // properties, not to an artificial floating-point volume model.
        var configuration = Deserialize($"{{\"SpeechVolume\":{input}}}");
        configuration.Validate();
        Assert.Equal(expected, configuration.SpeechVolume);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(42)]
    public void ValidateIsIdempotentForLegacyCurrentAndFutureVersions(int version)
    {
        var configuration = Deserialize(PreferenceJson(version).ToString(Formatting.None));
        configuration.FontSize = float.NaN;
        configuration.OutlineThickness = float.NegativeInfinity;
        configuration.SSAlertDuration = float.PositiveInfinity;
        configuration.TimeCalibrationOffset = float.MaxValue;
        configuration.SpeechVolume = -100;
        configuration.Validate();
        var first = JsonConvert.SerializeObject(configuration);
        for (var i = 0; i < 3; i++)
        {
            configuration.Validate();
            Assert.Equal(first, JsonConvert.SerializeObject(configuration));
        }
        Assert.Equal(Math.Max(4, version), configuration.Version);
        Assert.Equal(48f, configuration.FontSize);
        Assert.Equal(2f, configuration.OutlineThickness);
        Assert.Equal(5f, configuration.SSAlertDuration);
        Assert.Equal(20f, configuration.TimeCalibrationOffset);
        Assert.Equal(0, configuration.SpeechVolume);
    }

    [Fact]
    public void JsonNetFullRoundTripPreservesEveryPublicConfigurationProperty()
    {
        var expected = PreferenceJson(4);
        var configuration = Deserialize(expected.ToString(Formatting.None));
        configuration.Validate();
        var serialized = JsonConvert.SerializeObject(configuration);
        var actual = JObject.Parse(serialized);
        var properties = typeof(Configuration).GetProperties();
        Assert.Equal(17, properties.Length);
        Assert.Equal(properties.Select(p => p.Name).OrderBy(name => name),
            actual.Properties().Select(p => p.Name).OrderBy(name => name));
        Assert.True(JToken.DeepEquals(expected, actual));
        var restored = Deserialize(serialized);
        restored.Validate();
        Assert.True(JToken.DeepEquals(actual, JObject.FromObject(restored)));
    }

    [Fact]
    public void RealSaveDelegatesSameInstanceToTestBoundaryWithoutImplicitValidation()
    {
        Assert.Null(Services.PluginInterface.LastSavedConfiguration);
        Assert.Equal(0, Services.PluginInterface.SaveCallCount);
        var first = new Configuration { Version = 1, FontSize = -999 };
        first.Save();
        Assert.Same(first, Services.PluginInterface.LastSavedConfiguration);
        Assert.Equal(1, Services.PluginInterface.SaveCallCount);
        Assert.Equal(1, first.Version);
        Assert.Equal(-999f, first.FontSize);

        var second = Deserialize(PreferenceJson(4).ToString(Formatting.None));
        second.Save();
        Assert.Same(second, Services.PluginInterface.LastSavedConfiguration);
        Assert.Equal(2, Services.PluginInterface.SaveCallCount);
    }

    [Fact]
    public void LinkedModelImplementsIntVersionContractUsingOnlyTestAssemblyBoundary()
    {
        IPluginConfiguration configuration = new Configuration();
        configuration.Version = 42;
        Assert.Equal(42, configuration.Version);
        Assert.Equal(typeof(int), typeof(IPluginConfiguration).GetProperty(nameof(Configuration.Version))!.PropertyType);
        Assert.Equal(typeof(ConfigurationTests).Assembly, typeof(Configuration).Assembly);
        Assert.Equal(typeof(ConfigurationTests).Assembly, typeof(IPluginConfiguration).Assembly);
        Assert.Equal(typeof(ConfigurationTests).Assembly, typeof(Services).Assembly);
        Assert.DoesNotContain(typeof(ConfigurationTests).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "Dalamud" || assembly.Name == "ETDisplay");
    }

    private static Configuration Deserialize(string json) =>
        JsonConvert.DeserializeObject<Configuration>(json)
        ?? throw new InvalidOperationException("The JSON must deserialize to a configuration.");

    private static JObject PreferenceJson(int version) => new()
    {
        [nameof(Configuration.Version)] = version,
        [nameof(Configuration.SSAlertEnabled)] = false,
        [nameof(Configuration.SSTtsEnabled)] = false,
        [nameof(Configuration.SSAlertDuration)] = 12.5f,
        [nameof(Configuration.SSAlertColor)] = 0x12345678u,
        [nameof(Configuration.SpeechVolume)] = 37,
        [nameof(Configuration.CountdownEnabled)] = false,
        [nameof(Configuration.CountdownTtsEnabled)] = false,
        [nameof(Configuration.TimeCalibrationOffset)] = -7.25f,
        [nameof(Configuration.TtsEnabled)] = false,
        [nameof(Configuration.AutoClearOnSanctuary)] = false,
        [nameof(Configuration.Enabled)] = false,
        [nameof(Configuration.IsLocked)] = false,
        [nameof(Configuration.FontSize)] = 63.5f,
        [nameof(Configuration.TextColor)] = 0x89ABCDEFu,
        [nameof(Configuration.OutlineColor)] = 0xFEDCBA98u,
        [nameof(Configuration.OutlineThickness)] = 3.25f,
    };

    private static void AssertDefaults(Configuration configuration)
    {
        Assert.Equal(4, configuration.Version);
        Assert.True(configuration.SSAlertEnabled);
        Assert.True(configuration.SSTtsEnabled);
        Assert.Equal(5f, configuration.SSAlertDuration);
        Assert.Equal(0xFF00FFFFu, configuration.SSAlertColor);
        Assert.Equal(100, configuration.SpeechVolume);
        Assert.True(configuration.CountdownEnabled);
        Assert.True(configuration.CountdownTtsEnabled);
        Assert.Equal(0f, configuration.TimeCalibrationOffset);
        Assert.True(configuration.TtsEnabled);
        Assert.True(configuration.AutoClearOnSanctuary);
        Assert.True(configuration.Enabled);
        Assert.True(configuration.IsLocked);
        Assert.Equal(48f, configuration.FontSize);
        Assert.Equal(0xFFFFFF00u, configuration.TextColor);
        Assert.Equal(0xFF000000u, configuration.OutlineColor);
        Assert.Equal(2f, configuration.OutlineThickness);
    }
}
