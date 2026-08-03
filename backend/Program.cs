using backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<backend.Data.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

const string frontendCorsPolicy = "FrontendDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(frontendCorsPolicy);
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes Program to WebApplicationFactory for integration tests.
public partial class Program;