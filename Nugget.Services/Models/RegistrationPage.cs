using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationPage
{
  [JsonPropertyName("lower")]
  public string Lower { get; set; } = "";
  [JsonPropertyName("upper")]
  public string Upper { get; set; } = "";
  [JsonPropertyName("count")]
  public int Count { get; set; }
  [JsonPropertyName("items")]
  public List<RegistrationPageItem> Items { get; set; } = new();
}