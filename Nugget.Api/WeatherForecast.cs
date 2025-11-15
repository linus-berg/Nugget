namespace Nugget.Api;

public class WeatherForecast {
  public DateOnly date { get; set; }

  public int temperature_c { get; set; }

  public int temperature_f => 32 + (int)(temperature_c / 0.5556);

  public string? summary { get; set; }
}