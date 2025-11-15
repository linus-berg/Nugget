using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

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