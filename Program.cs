using System.Text;
using Bizkit_backend.Configuration;
using Bizkit_backend.Data;
using Bizkit_backend.Models.Entities;
using Bizkit_backend.Services.Auth;
using Bizkit_backend.Services.BusinessTypes;
using Bizkit_backend.Services.Categories;
using Bizkit_backend.Services.Cart;
using Bizkit_backend.Services.Orders;
using Bizkit_backend.Services.Payments;
using Bizkit_backend.Services.Products;
using Bizkit_backend.Services.Resale;
using Bizkit_backend.Services.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(
    "DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. " +
        "Add it to user secrets or appsettings.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key not found. Add 'Jwt:Key' to user secrets.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer not found. Add 'Jwt:Issuer' to user secrets.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience not found. Add 'Jwt:Audience' to user secrets.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)),

        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

builder.Services.Configure<AdminSettings>(
    builder.Configuration.GetSection("Admin"));

builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));

var cloudinarySettings = builder.Configuration
    .GetSection("Cloudinary")
    .Get<CloudinarySettings>()
    ?? throw new InvalidOperationException(
        "Cloudinary configuration not found.");

var cloudinaryAccount = new Account(
    cloudinarySettings.CloudName,
    cloudinarySettings.ApiKey,
    cloudinarySettings.ApiSecret);

var cloudinary = new Cloudinary(cloudinaryAccount);

builder.Services.AddSingleton(cloudinary);

builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAdminSetupService, AdminSetupService>();
builder.Services.AddScoped<IBusinessTypeService, BusinessTypeService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IResaleService, ResaleService>();

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    var databaseAvailable = await db.Database.CanConnectAsync(
        app.Lifetime.ApplicationStopping);

    if (databaseAvailable)
    {
        app.Logger.LogInformation(
            "Connected to PostgreSQL database.");

        var adminSetupService = scope.ServiceProvider
            .GetRequiredService<IAdminSetupService>();

        await adminSetupService.SetupAsync(
            app.Lifetime.ApplicationStopping);
    }
    else
    {
        app.Logger.LogWarning(
            "Could not connect to PostgreSQL database. " +
            "Admin setup was skipped.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();