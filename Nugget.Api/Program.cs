using Nugget.Services;

namespace Nugget.Api;

public class Program {
  public static void Main(string[] args) {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    builder.Services.AddControllers();
    // Add services to the container.
    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<PackageStorageService>();

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    WebApplication app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment()) {
      app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    string[] summaries = new[] {
      "Freezing",
      "Bracing",
      "Chilly",
      "Cool",
      "Mild",
      "Warm",
      "Balmy",
      "Hot",
      "Sweltering",
      "Scorching"
    };

    app.MapGet(
         "/weatherforecast",
         (HttpContext http_context) => {
           WeatherForecast[] forecast = Enumerable.Range(1, 5)
                                                  .Select(
                                                    index =>
                                                      new WeatherForecast {
                                                        date = DateOnly
                                                          .FromDateTime(
                                                            DateTime.Now
                                                              .AddDays(index)
                                                          ),
                                                        temperature_c =
                                                          Random.Shared.Next(
                                                            -20,
                                                            55
                                                          ),
                                                        summary =
                                                          summaries[
                                                            Random.Shared.Next(
                                                              summaries.Length
                                                            )]
                                                      }
                                                  )
                                                  .ToArray();
           return forecast;
         }
       )
       .WithName("GetWeatherForecast");
    app.MapControllers();
    app.Run();
  }
}