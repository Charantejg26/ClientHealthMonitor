using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using ClientHealthMonitor.Data;
using ClientHealthMonitor.Models;
using ClientHealthMonitor.Services;
using ClientHealthMonitor.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using CommonLib;

var builder = WebApplication.CreateBuilder(args);

// DI for services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddHttpClient<GoogleApiTokenService>();

// Add PostgreSQL connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreConnection")));


// JWT settings
var config = builder.Configuration;
var jwtSettings = config.GetSection("JwtSettings").Get<JWTSettings>();
if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.Key))
{
    throw new InvalidOperationException("JWT settings are missing or incomplete in configuration. Please check 'JwtSettings' section in appsettings.json.");
}
var key = Encoding.UTF8.GetBytes(jwtSettings.Key);

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };

    // CRITICAL: Tell .NET to look for the token in the Cookie
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            context.Token = context.Request.Cookies["X-Access-Token"];
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

//Add Cors
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyReactAppAt5173",
        builder =>
        {
            builder.WithOrigins("http://localhost:5173")
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials(); // If using cookies/auth
        });
});

//AWS
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
//builder.Services.AddAWSService<IAmazonS3>();
//builder.Services.AddScoped<IFileStorageService, S3FileStorageService>();


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Client Management API",
        Version = "v1"
    });

    // Add JWT Bearer definition
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid JWT token.",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var encryptKey = builder.Configuration["Encryption_TokenKey"];
if (string.IsNullOrEmpty(encryptKey))
{
    throw new InvalidOperationException("Encryption key is missing in configuration. Please check 'Encryption:TokenKey' in appsettings.json.");
}

builder.Services.AddSingleton<ITokenEncryptionService>(
    new EncryptDecryptToken(encryptKey));
// Initialize encryption helper early
//CryptoHelper.Initialize(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowMyReactAppAt5173");

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
