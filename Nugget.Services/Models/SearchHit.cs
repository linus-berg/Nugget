using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchHit
{
  [JsonPropertyName("id")]
  public string id { get; set; } = "";
  [JsonPropertyName("version")]
  public string version { get; set; } = "";
  [JsonPropertyName("description")]
  public string description { get; set; } = "";
  [JsonPropertyName("authors")]
  public string[] authors { get; set; } = Array.Empty<string>();
  [JsonPropertyName("versions")]
  public List<SearchVersion> versions { get; set; } = new();
}