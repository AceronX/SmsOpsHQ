using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class FeatureFlagsTests : IDisposable
{
    private readonly string _tempDirectory;

    public FeatureFlagsTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"smsopshq_flags_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    // =================================================================
    // Default Flags (no file)
    // =================================================================

    [Fact]
    public void Defaults_WhenNoFileExists_UsesDefaults()
    {
        string nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.json");
        FeatureFlags flags = new(nonExistentPath);

        Assert.True(flags.IsXpdEnabled);
        Assert.True(flags.IncludeXpdDefault);
        Assert.Equal(50, flags.XpdTimeoutMs);
        Assert.Equal(2, flags.MaxXpdConcurrency);
    }

    [Fact]
    public void Defaults_GetAllFlags_ContainsAllDefaults()
    {
        string nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.json");
        FeatureFlags flags = new(nonExistentPath);

        IReadOnlyDictionary<string, object> allFlags = flags.GetAllFlags();
        Assert.True(allFlags.ContainsKey("include_xpd_default"));
        Assert.True(allFlags.ContainsKey("xpd_enabled"));
        Assert.True(allFlags.ContainsKey("xpd_timeout_ms"));
        Assert.True(allFlags.ContainsKey("max_xpd_concurrency"));
    }

    // =================================================================
    // Load Flags from File
    // =================================================================

    [Fact]
    public void LoadFlags_ValidFile_OverridesDefaults()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_enabled": false,
            "xpd_timeout_ms": 100,
            "max_xpd_concurrency": 4
        }
        """);

        FeatureFlags flags = new(flagFile);

        Assert.False(flags.IsXpdEnabled);
        Assert.Equal(100, flags.XpdTimeoutMs);
        Assert.Equal(4, flags.MaxXpdConcurrency);
        // include_xpd_default should still be the default (true)
        Assert.True(flags.IncludeXpdDefault);
    }

    [Fact]
    public void LoadFlags_PartialOverride_MergesWithDefaults()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_timeout_ms": 200
        }
        """);

        FeatureFlags flags = new(flagFile);

        Assert.True(flags.IsXpdEnabled);         // default
        Assert.True(flags.IncludeXpdDefault);     // default
        Assert.Equal(200, flags.XpdTimeoutMs);    // overridden
        Assert.Equal(2, flags.MaxXpdConcurrency); // default
    }

    [Fact]
    public void LoadFlags_CustomFlags_AccessibleViaGetFlag()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "custom_feature": true,
            "custom_limit": 42
        }
        """);

        FeatureFlags flags = new(flagFile);

        Assert.Equal(true, flags.GetFlag("custom_feature"));
        Assert.Equal(42, flags.GetFlag("custom_limit"));
    }

    // =================================================================
    // Invalid JSON
    // =================================================================

    [Fact]
    public void LoadFlags_InvalidJson_UsesDefaults()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, "NOT VALID JSON {{{");

        FeatureFlags flags = new(flagFile);

        Assert.True(flags.IsXpdEnabled);
        Assert.Equal(50, flags.XpdTimeoutMs);
        Assert.Equal(2, flags.MaxXpdConcurrency);
    }

    [Fact]
    public void LoadFlags_EmptyFile_UsesDefaults()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, "");

        FeatureFlags flags = new(flagFile);

        Assert.True(flags.IsXpdEnabled);
    }

    // =================================================================
    // ReloadFlags
    // =================================================================

    [Fact]
    public void ReloadFlags_PicksUpChanges()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_enabled": true
        }
        """);

        FeatureFlags flags = new(flagFile);
        Assert.True(flags.IsXpdEnabled);

        // Update the file
        File.WriteAllText(flagFile, """
        {
            "xpd_enabled": false
        }
        """);

        flags.ReloadFlags();
        Assert.False(flags.IsXpdEnabled);
    }

    [Fact]
    public void ReloadFlags_FileMissing_UsesDefaults()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_timeout_ms": 200
        }
        """);

        FeatureFlags flags = new(flagFile);
        Assert.Equal(200, flags.XpdTimeoutMs);

        // Delete the file
        File.Delete(flagFile);

        flags.ReloadFlags();
        Assert.Equal(50, flags.XpdTimeoutMs); // back to default
    }

    // =================================================================
    // GetFlag<T> Typed Access
    // =================================================================

    [Fact]
    public void GetFlagTyped_IntegerFlag_ReturnsCorrectType()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_timeout_ms": 150
        }
        """);

        FeatureFlags flags = new(flagFile);
        int timeout = flags.GetFlag("xpd_timeout_ms", 50);
        Assert.Equal(150, timeout);
    }

    [Fact]
    public void GetFlagTyped_BooleanFlag_ReturnsCorrectType()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "xpd_enabled": false
        }
        """);

        FeatureFlags flags = new(flagFile);
        bool enabled = flags.GetFlag("xpd_enabled", true);
        Assert.False(enabled);
    }

    [Fact]
    public void GetFlagTyped_MissingFlag_ReturnsDefault()
    {
        string nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.json");
        FeatureFlags flags = new(nonExistentPath);

        string result = flags.GetFlag("nonexistent_flag", "fallback");
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void GetFlagTyped_StringFlag_ReturnsCorrectValue()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "environment": "production"
        }
        """);

        FeatureFlags flags = new(flagFile);
        string env = flags.GetFlag("environment", "development");
        Assert.Equal("production", env);
    }

    // =================================================================
    // Data Types in JSON
    // =================================================================

    [Fact]
    public void LoadFlags_MixedTypes_HandledCorrectly()
    {
        string flagFile = Path.Combine(_tempDirectory, "flags.json");
        File.WriteAllText(flagFile, """
        {
            "bool_flag": true,
            "int_flag": 42,
            "float_flag": 3.14,
            "string_flag": "hello"
        }
        """);

        FeatureFlags flags = new(flagFile);

        Assert.Equal(true, flags.GetFlag("bool_flag"));
        Assert.Equal(42, flags.GetFlag("int_flag"));
        Assert.IsType<double>(flags.GetFlag("float_flag"));
        Assert.Equal("hello", flags.GetFlag("string_flag"));
    }
}
