using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationIndexResponse
{
  [JsonPropertyName("count")]
  public int count { get; set; }
  [JsonPropertyName("items")]
  public List<RegistrationPage> items { get; set; } = new();
}