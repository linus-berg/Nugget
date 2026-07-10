using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchHit
{
  [JsonPropertyName("@id")]
  public string registration_id { get; set; } = "";
  [JsonPropertyName("@type")]
  public string type { get; set; } = "Package";
  [JsonPropertyName("registration")]
  public string registration { get; set; } = "";
  [JsonPropertyName("id")]
  public string id { get; set; } = "";
  [JsonPropertyName("version")]
  public string version { get; set; } = "";
  [JsonPropertyName("description")]
  public string description { get; set; } = "";
  [JsonPropertyName("authors")]
  public string[] authors { get; set; } = Array.Empty<string>();
  [JsonPropertyName("totalDownloads")]
  public int total_downloads { get; set; } = 0;
  [JsonPropertyName("verified")]
  public bool verified { get; set; } = false;
  [JsonPropertyName("versions")]
  public List<SearchVersion> versions { get; set; } = new();
}