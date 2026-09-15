using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InvestorChat.Client.Services;

public class AuthService
{
    private readonly HttpClient _http;

    public AuthService(HttpClient http)
    {
        _http = http;
    }

    public string? Token { get; private set; }
    public string? UserName { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UserName = username,
            Password = password
        });

        var result = await response.Content.ReadFromJsonAsync<AuthResult>();
        if (result is null)
        {
            throw new InvalidOperationException("Brak odpowiedzi z serwera.");
        }

        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        ApplyToken(result.Token!, result.UserName!);
        return result;
    }

    public async Task<AuthResult> RegisterAsync(string username, string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = username,
            Email = email,
            Password = password
        });

        var result = await response.Content.ReadFromJsonAsync<AuthResult>();
        if (result is null)
        {
            throw new InvalidOperationException("Brak odpowiedzi z serwera.");
        }

        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        ApplyToken(result.Token!, result.UserName!);
        return result;
    }

    public void Logout()
    {
        ApplyToken(null, null);
    }

    public event Action? AuthStateChanged;

    private void ApplyToken(string? token, string? userName)
    {
        Token = token;
        UserName = userName;

        if (string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Notify subscribers (UI) that auth state changed
        // Wrap invocation to avoid exceptions in subscriber handlers bubbling out to JS runtime
        try
        {
            AuthStateChanged?.Invoke();
        }
        catch
        {
            // Swallow subscriber exceptions to keep client runtime stable.
            // If you need to log these, inject ILogger<AuthService> and log here.
        }
    }
}


public class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class AuthResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? UserName { get; set; }
}
