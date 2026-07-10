using Minio;
using Nugget.Services;

namespace Nugget.Api;

public class Program {
  public static void Main(string[] args) {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    builder.Services.AddControllers();
    // Add services to the container.
    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();
    
    // Configure Minio
    builder.Services.AddMinio(configureSource => configureSource
        .WithEndpoint(builder.Configuration["Minio:Endpoint"])
        .WithCredentials(builder.Configuration["Minio:AccessKey"], builder.Configuration["Minio:SecretKey"])
        .WithSSL(builder.Configuration.GetValue<bool?>("Minio:Secure") ?? false)
    );

    builder.Services.AddScoped<PackageStorageService>();

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    WebApplication app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment()) {
      app.MapOpenApi();
    }

    // Don't force HTTPS redirection — NuGet clients often use HTTP for private feeds
    // and the test scripts use http://localhost. HTTPS should be handled by a reverse proxy.
    // app.UseHttpsRedirection();

    app.UseAuthorization();
    app.MapControllers();
    app.Run();
  }
}