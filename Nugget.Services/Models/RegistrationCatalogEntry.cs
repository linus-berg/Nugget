using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationCatalogEntry
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("version")]
  public string version { get; set; } = "";
  [JsonPropertyName("description")]
  public string description { get; set; } = "";
  [JsonPropertyName("packageContent")]
  public string package_content { get; set; } = "";
}