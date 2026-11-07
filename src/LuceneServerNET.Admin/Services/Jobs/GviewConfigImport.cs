using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Repository;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuceneServerNET.Admin.Services.Jobs;

#region gView FillLuceneServer configuration (only the parts that are imported)

public class GviewConfig
{
    [JsonPropertyName("lucene_server_connection")] public GviewConnection? Connection { get; set; }
    [JsonPropertyName("datasets")] public List<GviewDataset>? Datasets { get; set; }
}

public class GviewConnection
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("default_index")] public string? DefaultIndex { get; set; }
    [JsonPropertyName("delete_index")] public bool DeleteIndex { get; set; }
    [JsonPropertyName("encode_characters")] public string? EncodeCharacters { get; set; }
    [JsonPropertyName("phonetic_algorithm")] public string? PhoneticAlgorithm { get; set; }
}

public class GviewDataset
{
    [JsonPropertyName("feature_classes")] public List<GviewFeatureClass>? FeatureClasses { get; set; }
}

public class GviewFeatureClass
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("srs")] public int Srs { get; set; }
    [JsonPropertyName("objectid_fieldname")] public string? ObjectIdField { get; set; }
    [JsonPropertyName("geometry")] public bool Geometry { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("filter")] public string? Filter { get; set; }
    [JsonPropertyName("index_proto")] public GviewProto? Proto { get; set; }
    [JsonPropertyName("metadata")] public GviewMeta? Metadata { get; set; }
    [JsonPropertyName("replace")] public List<GviewReplace>? Replace { get; set; }
}

public class GviewProto
{
    [JsonPropertyName("suggested_text")] public string? SuggestedText { get; set; }
    [JsonPropertyName("subtext")] public string? SubText { get; set; }
    [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
}

public class GviewMeta
{
    [JsonPropertyName("sample")] public string? Sample { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("service")] public string? Service { get; set; }
    [JsonPropertyName("query")] public string? Query { get; set; }
}

public class GviewReplace
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
}

#endregion

/// <summary>A feature class of the configuration and the query that replaces it.</summary>
public class GviewImportSource
{
    public GviewFeatureClass FeatureClass { get; set; } = new();
    public string Sql { get; set; } = "";
}

/// <summary>
/// Creates an import job from a gView FillLuceneServer configuration (JSON): one source per feature class with the
/// webgis template, templates (index_proto), category, replacements and metadata. Connection strings of the
/// configuration are not used: all sources read from a database connection of the admin application.
/// </summary>
public static class GviewConfigImport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static GviewConfig Parse(string json)
    {
        GviewConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GviewConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid JSON: {ex.Message}");
        }

        if (config?.Datasets is null || !config.Datasets.Any(d => d.FeatureClasses?.Count > 0))
        {
            throw new FormatException("No feature classes found (datasets[].feature_classes[])");
        }
        return config;
    }

    public static IEnumerable<GviewFeatureClass> FeatureClasses(GviewConfig config)
        => config.Datasets!.SelectMany(d => d.FeatureClasses ?? []);

    /// <summary>"SELECT * FROM {name} WHERE {filter}".</summary>
    public static string ProposeSql(GviewFeatureClass featureClass)
        => $"SELECT * FROM {featureClass.Name}" + (String.IsNullOrWhiteSpace(featureClass.Filter) ? "" : $" WHERE {featureClass.Filter}");

    public static string? IndexName(GviewConfig config)
    {
        var name = config.Connection?.DefaultIndex?.Trim().ToLowerInvariant();
        return String.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Builds the job (not saved). <paramref name="readSample"/> reads sample rows of a query; a source whose query
    /// fails is created without mapping (a warning is returned, the mapping is detected later on the job page).
    /// </summary>
    public static async Task<(ImportJobRecord Job, ImportJobMapping Settings, List<string> Warnings)> BuildJobAsync(
        GviewConfig config, IReadOnlyList<GviewImportSource> sources, long connectionId, string instance, string jobName,
        Func<string, Task<IReadOnlyList<IDictionary<string, object?>>>> readSample)
    {
        var warnings = new List<string>();
        var target = IndexName(config) ?? jobName.Trim().ToLowerInvariant().Replace(' ', '_');

        var settings = new ImportJobMapping
        {
            EncodeCharacters = String.IsNullOrWhiteSpace(config.Connection?.EncodeCharacters) ? null : config.Connection!.EncodeCharacters,
            Phonetics = Enum.TryParse<Core.Phonetics.Algorithm>(config.Connection?.PhoneticAlgorithm, ignoreCase: true, out var phonetics) ? phonetics : Core.Phonetics.Algorithm.None
        };
        if (!String.IsNullOrWhiteSpace(config.Connection?.PhoneticAlgorithm) && settings.Phonetics == Core.Phonetics.Algorithm.None &&
            !config.Connection!.PhoneticAlgorithm!.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Unknown phonetic algorithm '{config.Connection.PhoneticAlgorithm}': None");
        }

        foreach (var item in sources)
        {
            var featureClass = item.FeatureClass;
            var source = new ImportJobSource
            {
                Name = String.IsNullOrWhiteSpace(featureClass.Category) ? featureClass.Name : featureClass.Category!,
                ConnectionId = connectionId,
                Sql = item.Sql,
                Preset = WebgisPreset.Name,
                Metadata = new JobSourceMetadata
                {
                    Category = featureClass.Category,
                    Sample = featureClass.Metadata?.Sample,
                    Description = featureClass.Metadata?.Description,
                    Service = featureClass.Metadata?.Service,
                    Query = featureClass.Metadata?.Query
                }
            };
            settings.Sources.Add(source);

            IReadOnlyList<IDictionary<string, object?>> sample;
            try
            {
                sample = await readSample(item.Sql);
                if (sample.Count == 0) throw new InvalidOperationException("the query returns no rows");
            }
            catch (Exception ex)
            {
                warnings.Add($"{source.Name}: {ex.Message} – detect the mapping on the job page");
                continue;
            }

            var columns = sample.SelectMany(r => r.Keys).Distinct().ToList();
            var mapping = MappingInference.Infer(sample);

            var idColumn = !String.IsNullOrWhiteSpace(featureClass.ObjectIdField)
                ? columns.FirstOrDefault(c => c.Equals(featureClass.ObjectIdField, StringComparison.OrdinalIgnoreCase))
                : WebgisPreset.ProposeIdColumn(mapping, columns);
            if (idColumn is null)
            {
                warnings.Add($"{source.Name}: no ID column found – choose one on the job page");
                idColumn = columns[0];
            }

            if (!featureClass.Geometry)
            {
                mapping.GeoFields.Clear();
            }
            else if (mapping.GeoFields.Count == 0)
            {
                warnings.Add($"{source.Name}: no geometry column found");
            }
            else if (featureClass.Srs > 0)
            {
                // the SRID of the value (EWKB) wins anyway; for WKB / x,y columns the srs of the configuration is used
                mapping.GeoFields[0].Epsg = featureClass.Srs;
            }

            WebgisPreset.Apply(mapping, idColumn);
            mapping.Fields.First(f => f.Name == WebgisPreset.SuggestedTextField).Template = featureClass.Proto?.SuggestedText ?? "";
            mapping.Fields.First(f => f.Name == WebgisPreset.SubTextField).Template = featureClass.Proto?.SubText ?? "";
            mapping.Fields.First(f => f.Name == WebgisPreset.ThumbnailField).Template = featureClass.Proto?.ThumbnailUrl ?? "";
            mapping.Replacements = (featureClass.Replace ?? []).Select(r => new TextReplacement { From = r.From, To = r.To }).ToList();

            foreach (var field in mapping.Fields.Where(f => f.IsTemplate))
            {
                var unknown = FieldTemplate.UnknownPlaceholders(field.Template, columns).ToList();
                if (unknown.Count > 0) warnings.Add($"{source.Name}: {field.Name} uses unknown columns {String.Join(", ", unknown)}");
            }

            source.Mapping = mapping;
        }

        var job = new ImportJobRecord
        {
            Name = jobName.Trim(),
            Instance = instance,
            ConnectionId = connectionId,
            Sql = settings.Sources.FirstOrDefault()?.Sql ?? "",
            Target = target,
            Mode = config.Connection?.DeleteIndex == false ? ImportJobModes.Append : ImportJobModes.ReplaceAlias,
            Cron = "",
            TimeZone = TimeZoneInfo.Local.Id,
            MappingJson = settings.ToJson()
        };
        return (job, settings, warnings);
    }
}
