using BeatBay.API.Settings;
using BeatBay.Data;
using BeatBay.Model;
using BeatBay.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Swagger/OpenAPI Configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar DbContext con PostgreSQL
builder.Services.AddDbContext<BeatBayDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BeatBayDbContext")));

// Configurar Identity con roles personalizados, confirmaci�n de cuenta y correo �nico
builder.Services.AddIdentity<User, Role>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true; // Evita registros con el mismo correo

    // Configuraci�n de contrase�a
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    // Configuraci�n de lockout
    options.Lockout.MaxFailedAccessAttempts = 5;  // N�mero m�ximo de intentos fallidos antes de bloquear la cuenta
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);  // Tiempo de bloqueo (1 minuto)
    options.Lockout.AllowedForNewUsers = true;  // Permite el lockout para usuarios nuevos
})
.AddEntityFrameworkStores<BeatBayDbContext>()
.AddDefaultTokenProviders();

// Registrar servicios personalizados
builder.Services.AddTransient<IEmailSender, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>(); // Registrar JWT Service

// Configuraci�n de autenticaci�n JWT
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
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ClockSkew = TimeSpan.Zero // Eliminar tolerancia de tiempo
    };
});

//agregar para azure

builder.Services.Configure<AzureBlobStorageSettings>(builder.Configuration.GetSection("AzureBlobStorageSettings"));


// Configuraci�n de CORS para permitir el acceso desde el frontend MVC
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWeb",
        policy => policy
            .WithOrigins("https://localhost:7194")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Agregar servicios adicionales
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configuraci�n del middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BeatBay API V1");
    });
}


app.UseHttpsRedirection();
app.UseStaticFiles();  // Si necesitas servir archivos est�ticos

app.UseRouting();

// Configuraci�n de CORS
app.UseCors("AllowWeb");

// Middleware para autenticaci�n y autorizaci�n
app.UseAuthentication();
app.UseAuthorization();

// Mapeo de controladores
app.MapControllers();

app.Run();