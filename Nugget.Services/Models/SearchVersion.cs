using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchVersion
{
  [JsonPropertyName("version")]
  public string version { get; set; } = "";
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("downloads")]
  public int downloads { get; set; } = 0;
}