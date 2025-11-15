namespace Nugget.Services.Models;
using System.Text.Json.Serialization;

// --- Shared Metadata ---
public class PackageMetadata
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Authors { get; set; } = "";
}

// --- v3/index.json Models ---
public class ServiceIndexResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "3.0.0";
    [JsonPropertyName("resources")]
    public List<ServiceResource> Resources { get; set; } = new();
}

public class ServiceResource
{
    [JsonPropertyName("@id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "";
}

// --- v3/search Models ---
public class SearchResponse
{
    [JsonPropertyName("totalHits")]
    public int TotalHits { get; set; }
    [JsonPropertyName("data")]
    public List<SearchHit> Data { get; set; } = new();
}

public class SearchHit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    [JsonPropertyName("authors")]
    public string[] Authors { get; set; } = Array.Empty<string>();
    [JsonPropertyName("versions")]
    public List<SearchVersion> Versions { get; set; } = new();
}

public class SearchVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    [JsonPropertyName("@id")]
    public string Id { get; set; } = "";
}

// --- v3/registration Models ---
public class RegistrationIndexResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
    [JsonPropertyName("items")]
    public List<RegistrationPage> Items { get; set; } = new();
}

public class RegistrationPage
{
    [JsonPropertyName("lower")]
    public string Lower { get; set; } = "";
    [JsonPropertyName("upper")]
    public string Upper { get; set; } = "";
    [JsonPropertyName("count")]
    public int Count { get; set; }
    [JsonPropertyName("items")]
    public List<RegistrationPageItem> Items { get; set; } = new();
}

public class RegistrationPageItem
{
    [JsonPropertyName("catalogEntry")]
    public RegistrationCatalogEntry CatalogEntry { get; set; } = new();
}

public class RegistrationCatalogEntry
{
    [JsonPropertyName("@id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    [JsonPropertyName("packageContent")]
    public string PackageContent { get; set; } = "";
}
