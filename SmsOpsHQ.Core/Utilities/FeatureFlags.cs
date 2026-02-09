using System.Text.Json;

namespace SmsOpsHQ.Core.Utilities;

// Feature flags for operational control. Flags can be changed at runtime
// by editing a JSON file (no redeploy required).
//
// Default flags provide safe values so the system works even if the
// feature_flags.json file does not exist.
public sealed class FeatureFlags
{
    private static readonly Dictionary<string, object> DefaultFlags = new()
    {
        ["include_xpd_default"] = true,
        ["xpd_enabled"] = true,
        ["xpd_timeout_ms"] = 50,
        ["max_xpd_concurrency"] = 2
    };

    private Dictionary<string, object> _flags;
    private readonly string _flagFilePath;

    public FeatureFlags(string? flagFilePath = null)
    {
        _flagFilePath = flagFilePath
            ?? Environment.GetEnvironmentVariable("FEATURE_FLAGS_FILE")
            ?? "feature_flags.json";
        _flags = new Dictionary<string, object>(DefaultFlags);
        LoadFlags();
    }

    // Load feature flags from the JSON file. If the file does not exist
    // or is invalid JSON, default values are used.
    public void LoadFlags()
    {
        Dictionary<string, object> newFlags = new(DefaultFlags);

        if (!File.Exists(_flagFilePath))
        {
            _flags = newFlags;
            return;
        }

        try
        {
            string json = File.ReadAllText(_flagFilePath);
            Dictionary<string, JsonElement>? loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (loaded is not null)
            {
                foreach (KeyValuePair<string, JsonElement> kvp in loaded)
                {
                    newFlags[kvp.Key] = ConvertJsonElement(kvp.Value);
                }
            }

            _flags = newFlags;
        }
        catch (JsonException)
        {
            _flags = newFlags;
        }
        catch (IOException)
        {
            _flags = newFlags;
        }
    }

    // Reload flags from the file. Call this to pick up changes
    // without restarting the server.
    public void ReloadFlags()
    {
        LoadFlags();
    }

    // Get a feature flag value, or the default if not found.
    public object? GetFlag(string name, object? defaultValue = null)
    {
        return _flags.TryGetValue(name, out object? value) ? value : defaultValue;
    }

    // Get a feature flag as a typed value.
    public T GetFlag<T>(string name, T defaultValue)
    {
        if (!_flags.TryGetValue(name, out object? value))
            return defaultValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    // Check if XPD queries are enabled globally.
    public bool IsXpdEnabled => GetFlag("xpd_enabled", true);

    // Get default value for include_xpd parameter.
    public bool IncludeXpdDefault => GetFlag("include_xpd_default", true);

    // Get XPD semaphore timeout in milliseconds.
    public int XpdTimeoutMs => GetFlag("xpd_timeout_ms", 50);

    // Get maximum concurrent XPD queries.
    public int MaxXpdConcurrency => GetFlag("max_xpd_concurrency", 2);

    // Get all feature flags as a read-only dictionary.
    public IReadOnlyDictionary<string, object> GetAllFlags()
    {
        return new Dictionary<string, object>(_flags);
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out int intVal) => intVal,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.ToString()
        };
    }
}
