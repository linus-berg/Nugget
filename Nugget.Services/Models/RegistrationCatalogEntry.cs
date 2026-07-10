using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationCatalogEntry
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("@type")]
  public string type { get; set; } = "PackageDetails";
  [JsonPropertyName("id")]
  public string package_id { get; set; } = "";
  [JsonPropertyName("version")]
  public string version { get; set; } = "";
  [JsonPropertyName("description")]
  public string description { get; set; } = "";
  [JsonPropertyName("authors")]
  public string authors { get; set; } = "";
  [JsonPropertyName("packageContent")]
  public string package_content { get; set; } = "";
  [JsonPropertyName("listed")]
  public bool listed { get; set; } = true;
}