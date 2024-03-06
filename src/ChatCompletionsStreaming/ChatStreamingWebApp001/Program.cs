using Microsoft.Extensions.Azure;

namespace ChatStreamingWebApp001;
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "[HH:mm:ss.fff] ";
        }).AddDebug();

        // Add services to the container.
        builder.Services.AddSingleton<AzureEventSourceLogForwarder>();

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();        

        app.UseAuthorization();


        app.MapControllers();

        var azureEventSourceLogForwarder = app.Services.GetService<AzureEventSourceLogForwarder>();
        azureEventSourceLogForwarder?.Start();

        app.Run();
    }
}
