using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchResponse
{
  [JsonPropertyName("totalHits")]
  public int TotalHits { get; set; }
  [JsonPropertyName("data")]
  public List<SearchHit> Data { get; set; } = new();
}