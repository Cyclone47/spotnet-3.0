using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NLog;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// Persists and loads user-created custom filters from a JSON file in the app data folder.
/// </summary>
public sealed class CustomFilterService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly string _filePath;

    public CustomFilterService(IAppPaths appPaths)
    {
        _filePath = Path.Combine(appPaths.DataFolder, "custom_filters.json");
    }

    public List<CustomFilterDefinition> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonConvert.DeserializeObject<List<CustomFilterDefinition>>(json);
                if (list != null) return list;
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load custom filters from {0}, returning empty list.", _filePath);
        }

        return [];
    }

    public void Save(IEnumerable<CustomFilterDefinition> filters)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(filters, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save custom filters to {0}", _filePath);
        }
    }
}

/// <summary>
/// Serializable form of a user-created custom filter.
/// </summary>
public sealed class CustomFilterDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🔖";
    public int? CategoryId { get; set; }
    public string? SubcatTag { get; set; }
    public int? MaxAgeHours { get; set; }
    public string? KeywordFilter { get; set; }
}
