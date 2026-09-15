using System.Security.Claims;
using System.Text;
using InvestorChat.Server.Data;
using InvestorChat.Server.Hubs;
using InvestorChat.Server.Models;
using InvestorChat.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=chatapp.db"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5207",
                "https://localhost:7098",
                "http://localhost:5268",
                "https://localhost:7022")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddHttpClient<InvestorChat.Server.Services.MarketPriceService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("InvestorChat/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.Referrer = new Uri("https://finance.yahoo.com/");
});
builder.Services.AddSingleton<PasswordHasher<UserAccount>>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddHostedService<InvestorChat.Server.Services.MarketPriceService>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKeyForInvestorChat1234567890";
var issuer = builder.Configuration["Jwt:Issuer"] ?? "InvestorChat";
var audience = builder.Configuration["Jwt:Audience"] ?? "InvestorChatUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors("ClientPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/register", async (RegisterRequest request, AppDbContext db, PasswordHasher<UserAccount> passwordHasher, TokenService tokenService) =>
{
    if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Nazwa użytkownika i hasło są wymagane." });
    }

    var trimmedUserName = request.UserName.Trim();
    if (trimmedUserName.Length < 3)
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Nazwa użytkownika musi mieć co najmniej 3 znaki." });
    }

    if (request.Password.Length < 6)
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Hasło musi mieć co najmniej 6 znaków." });
    }

    if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@'))
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Email ma niepoprawny format." });
    }

    var email = string.IsNullOrWhiteSpace(request.Email) ? $"{trimmedUserName}@demo.local" : request.Email.Trim();

    if (await db.Users.AnyAsync(u => u.UserName == trimmedUserName))
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Użytkownik o tej nazwie już istnieje." });
    }

    if (await db.Users.AnyAsync(u => u.Email == email))
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Adres email jest już używany." });
    }

    var user = new UserAccount
    {
        UserName = trimmedUserName,
        Email = email,
        PasswordHash = passwordHasher.HashPassword(new UserAccount(), request.Password)
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    var token = tokenService.CreateToken(user);
    return Results.Ok(new AuthResult { Success = true, Token = token, UserName = user.UserName });
});

app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext db, PasswordHasher<UserAccount> passwordHasher, TokenService tokenService) =>
{
    if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Nazwa użytkownika i hasło są wymagane." });
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == request.UserName.Trim());
    if (user is null)
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Nieprawidłowe dane logowania." });
    }

    var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (verification == PasswordVerificationResult.Failed)
    {
        return Results.Ok(new AuthResult { Success = false, Message = "Nieprawidłowe dane logowania." });
    }

    var token = tokenService.CreateToken(user);
    return Results.Ok(new AuthResult { Success = true, Token = token, UserName = user.UserName });
});

app.MapGet("/api/auth/me", [Authorize] (ClaimsPrincipal user) =>
    Results.Ok(new { userName = user.Identity?.Name ?? user.FindFirst("username")?.Value }))
    .RequireAuthorization();

app.MapHub<ChatHub>("/chathub").RequireAuthorization();
app.MapHub<MarketHub>("/markethub");
app.MapFallbackToFile("index.html");

app.Run();
