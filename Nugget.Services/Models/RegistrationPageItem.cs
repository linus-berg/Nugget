using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationPageItem
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("packageContent")]
  public string package_content { get; set; } = "";
  [JsonPropertyName("catalogEntry")]
  public RegistrationCatalogEntry catalog_entry { get; set; } = new();
}