using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationIndexResponse
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("@type")]
  public List<string> type { get; set; } = new() { "catalog:CatalogRoot", "PackageRegistration", "catalog:Permalink" };
  [JsonPropertyName("count")]
  public int count { get; set; }
  [JsonPropertyName("items")]
  public List<RegistrationPage> items { get; set; } = new();
}