using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationPageItem
{
  [JsonPropertyName("catalogEntry")]
  public RegistrationCatalogEntry CatalogEntry { get; set; } = new();
}