using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchResponse {
  [JsonPropertyName("totalHits")]
  public int total_hits { get; set; }

  [JsonPropertyName("data")]
  public List<SearchHit> data { get; set; } = new();
}