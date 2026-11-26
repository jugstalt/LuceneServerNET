using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Core.Models.Mapping;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Jobs;

/// <summary>
/// One source of an import job: a query on a database connection and its mapping. All sources of a job are
/// imported into the same index (generation), each with its own metadata entry (key = <see cref="MetaId"/>).
/// </summary>
public class ImportJobSource
{
    public string Name { get; set; } = "";
    public long ConnectionId { get; set; }
    public string Sql { get; set; } = "";
    public InferredMapping Mapping { get; set; } = new();

    /// <summary>Stable id of the source (32 hex characters): metadata key and template variable {@metaid}.</summary>
    public string MetaId { get; set; } = NewMetaId();

    public JobSourceMetadata Metadata { get; set; } = new();

    /// <summary>Template the mapping was created from ("webgis"), null = free mapping.</summary>
    public string? Preset { get; set; }

    /// <summary>
    /// Append or update: key of the documents to delete for a trigger {"delete": [{"oid": 5}]}, built from the
    /// parameters, e.g. "{@metaid}.{@oid}". null = this source can not delete.
    /// </summary>
    public string? DeleteTemplate { get; set; }

    /// <summary>Parameters of the query (@name), lower case.</summary>
    public IReadOnlyList<string> Parameters => QueryParameters.Names(Sql);

    /// <summary>Parameters used by the delete template ({@oid} => oid).</summary>
    public IReadOnlyList<string> DeleteParameters
        => FieldTemplate.Placeholders(DeleteTemplate).Where(p => p.StartsWith('@') && !p.Equals(FieldTemplate.MetaIdVariable, StringComparison.OrdinalIgnoreCase)
                                                                 && !p.Equals(FieldTemplate.CategoryVariable, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p[1..].ToLowerInvariant()).Distinct().ToList();

    /// <summary>Source variables plus the parameters as template variables ({@oid}).</summary>
    public IReadOnlyDictionary<string, string> VariablesWith(IReadOnlyDictionary<string, object?>? parameters)
    {
        var variables = new Dictionary<string, string>(Variables, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in parameters ?? new Dictionary<string, object?>())
        {
            variables["@" + name] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
        return variables;
    }

    /// <summary>
    /// The source handles a trigger with these parameters: its query (or, for deletes, its delete template) uses
    /// parameters and all of them are given; with a source name only that source.
    /// </summary>
    public bool Accepts(IReadOnlyCollection<string> parameterNames, string? sourceName, bool delete)
    {
        if (!String.IsNullOrWhiteSpace(sourceName) && !DisplayName.Equals(sourceName.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        var required = delete ? (DeleteTemplate is null ? [] : DeleteParameters) : Parameters;
        return required.Count > 0 && required.All(r => parameterNames.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    public static string NewMetaId() => Guid.NewGuid().ToString("N");

    public static bool IsMetaId(string? value) => value is { Length: 32 } && Regex.IsMatch(value, "^[0-9a-f]{32}$");

    public bool HasMapping => Mapping.Fields.Count > 0 || Mapping.GeoFields.Count > 0;

    /// <summary>Template variables of the source.</summary>
    public IReadOnlyDictionary<string, string> Variables => new Dictionary<string, string>
    {
        [FieldTemplate.MetaIdVariable] = MetaId,
        [FieldTemplate.CategoryVariable] = Metadata.Category ?? ""
    };

    /// <summary>Source columns known from the mapping (preview not loaded).</summary>
    public IEnumerable<string> MappedColumns
        => Mapping.Fields.Where(f => !f.IsTemplate).Select(f => f.Column)
                  .Concat(Mapping.GeoFields.SelectMany(g => new[] { g.XColumn, g.YColumn, g.GeometryColumn }).OfType<string>())
                  .Distinct();

    public string DisplayName => String.IsNullOrWhiteSpace(Name) ? (Metadata.Category is { Length: > 0 } c ? c : "source") : Name;
}

/// <summary>
/// Metadata entry of a source, stored in the index under the meta id. The standard fields are the ones of the
/// gView FillLuceneServer tool (read by webgis: id, category, sample, description, service, query).
/// </summary>
public class JobSourceMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string? Category { get; set; }
    public string? Sample { get; set; }
    public string? Description { get; set; }
    public string? Service { get; set; }
    public string? Query { get; set; }

    /// <summary>Additional keys.</summary>
    public List<TextReplacement> Extra { get; set; } = [];

    public bool IsEmpty => new[] { Category, Sample, Description, Service, Query }.All(String.IsNullOrWhiteSpace)
                           && Extra.All(e => String.IsNullOrWhiteSpace(e.From));

    /// <summary>JSON of the metadata entry: {"id", "category", "sample", "description", "service", "query", ...}.</summary>
    public string ToJson(string metaId)
    {
        var values = new Dictionary<string, string>
        {
            ["id"] = metaId,
            ["category"] = Category ?? "",
            ["sample"] = Sample ?? "",
            ["description"] = Description ?? "",
            ["service"] = Service ?? "",
            ["query"] = Query ?? ""
        };
        foreach (var extra in Extra.Where(e => !String.IsNullOrWhiteSpace(e.From)))
        {
            values[extra.From.Trim()] = extra.To ?? "";
        }
        return JsonSerializer.Serialize(values, JsonOptions);
    }
}

/// <summary>The "webgis" template: the search index structure of webgis (formerly gView FillLuceneServer).</summary>
public static class WebgisPreset
{
    public const string Name = "webgis";

    public const string IdField = "id", SuggestedTextField = "suggested_text", SubTextField = "subtext",
                        CategoryField = "category", ThumbnailField = "thumbnail_url", GeoField = "geo", BBoxField = "bbox";

    private static readonly string[] IdColumnNames = ["objectid", "oid", "fid", "gid", "id"];

    /// <summary>Proposed id column: objectid, oid, fid, gid, id, else the first integer column.</summary>
    public static string? ProposeIdColumn(InferredMapping inferred, IEnumerable<string> columns)
    {
        var list = columns.ToList();
        foreach (var name in IdColumnNames)
        {
            var match = list.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return inferred.Fields.FirstOrDefault(f => !f.IsTemplate && f.FieldType == FieldTypes.Int32Type && list.Contains(f.Column))?.Column;
    }

    /// <summary>
    /// Replaces the mapping by the webgis structure. Columns stay in the mapping, but are not imported;
    /// the first geo source becomes "geo" (+ "bbox"). Existing templates of the webgis fields are kept.
    /// </summary>
    public static void Apply(InferredMapping mapping, string idColumn)
    {
        string? Existing(string field) => mapping.Fields.FirstOrDefault(f => f.IsTemplate && f.Name == field)?.Template;

        var suggested = Existing(SuggestedTextField) ?? "";
        var subtext = Existing(SubTextField) ?? "";
        var thumbnail = Existing(ThumbnailField) ?? "";

        mapping.Fields.RemoveAll(f => f.IsTemplate);
        foreach (var field in mapping.Fields)
        {
            field.Skip = true;
            field.Primary = false;
        }

        mapping.Fields.InsertRange(0,
        [
            new InferredField { Name = IdField, Template = $"{{{FieldTemplate.MetaIdVariable}}}.{{{idColumn}}}", FieldType = FieldTypes.StringType },
            new InferredField { Name = SuggestedTextField, Template = suggested, FieldType = FieldTypes.TextType, Primary = true },
            new InferredField { Name = SubTextField, Template = subtext, FieldType = FieldTypes.TextType, Primary = true },
            new InferredField { Name = CategoryField, Template = $"{{{FieldTemplate.CategoryVariable}}}", FieldType = FieldTypes.TextType, Primary = true },
            new InferredField { Name = ThumbnailField, Template = thumbnail, FieldType = FieldTypes.StringType, Index = false },
        ]);

        var geo = mapping.GeoFields.FirstOrDefault();
        mapping.GeoFields.Clear();
        if (geo != null)
        {
            geo.FieldName = GeoField;
            geo.BBoxFieldName = BBoxField;
            geo.StoreGeoJson = false;
            mapping.GeoFields.Add(geo);
        }
    }

    /// <summary>Checks a source with the webgis template; returns an error message or null.</summary>
    public static string? Validate(ImportJobSource source)
    {
        var fields = source.Mapping.Fields.Where(f => !f.Skip).ToList();
        var id = fields.FirstOrDefault(f => f.Name == IdField);
        if (id?.Template is null || !id.Template.Contains($"{{{FieldTemplate.MetaIdVariable}}}", StringComparison.OrdinalIgnoreCase))
        {
            return $"{source.DisplayName}: the field 'id' must be a template with {{@metaid}} (webgis reads the metadata of a hit from it)";
        }
        if (String.IsNullOrWhiteSpace(fields.FirstOrDefault(f => f.Name == SuggestedTextField)?.Template))
        {
            return $"{source.DisplayName}: the template of 'suggested_text' is empty";
        }
        if (String.IsNullOrWhiteSpace(source.Metadata.Category))
        {
            return $"{source.DisplayName}: the category is required";
        }
        return null;
    }
}
