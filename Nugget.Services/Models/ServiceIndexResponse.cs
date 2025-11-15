using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class ServiceIndexResponse {
  [JsonPropertyName("version")]
  public string version { get; set; } = "3.0.0";

  [JsonPropertyName("resources")]
  public List<ServiceResource> resources { get; set; } = new();
}