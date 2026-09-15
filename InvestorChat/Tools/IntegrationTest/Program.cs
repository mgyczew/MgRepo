using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json.Serialization;

Console.WriteLine("Integration test starting...");

var serverBase = "http://localhost:5207";
using var http = new HttpClient { BaseAddress = new Uri(serverBase) };

var user = "testuser_" + Guid.NewGuid().ToString("N").Substring(0, 6);
var email = user + "@example.local";
var password = "Password123!";

try
{
    Console.WriteLine($"Registering user {user}...");
    var regResp = await http.PostAsJsonAsync("/api/auth/register", new { UserName = user, Email = email, Password = password });
    var reg = await regResp.Content.ReadFromJsonAsync<AuthResult>();
    Console.WriteLine($"Register result: Success={reg?.Success}, Message={reg?.Message}");

    Console.WriteLine("Logging in...");
    var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { UserName = user, Password = password });
    var login = await loginResp.Content.ReadFromJsonAsync<AuthResult>();
    Console.WriteLine($"Login result: Success={login?.Success}, Message={login?.Message}");

    var token = login?.Token;

    if (string.IsNullOrWhiteSpace(token))
    {
        Console.WriteLine("No token returned, aborting.");
        return;
    }

    Console.WriteLine("Starting SignalR connections...");

    var chatConn = new HubConnectionBuilder()
        .WithUrl(serverBase + "/chathub", options => {
            options.AccessTokenProvider = () => Task.FromResult(token);
        })
        .WithAutomaticReconnect()
        .Build();

    chatConn.On<string, string, string>("ReceiveMessage", (userName, text, timestamp) => {
        Console.WriteLine($"[CHAT] {timestamp} {userName}: {text}");
    });

    var marketConn = new HubConnectionBuilder()
        .WithUrl(serverBase + "/markethub")
        .WithAutomaticReconnect()
        .Build();

    marketConn.On<string, decimal, decimal, string>("ReceiveQuote", (sym, price, change, time) => {
        Console.WriteLine($"[MARKET] {time} {sym} {price} ({(change>=0?"+":"")}{change})");
    });

    await Task.WhenAll(chatConn.StartAsync(), marketConn.StartAsync());
    Console.WriteLine("Connections started. Sending test chat message...");

    await chatConn.SendAsync("SendMessage", "Hello from integration test");

    Console.WriteLine("Listening for messages for 8 seconds...");
    await Task.Delay(8000);

    await Task.WhenAll(chatConn.StopAsync(), marketConn.StopAsync());

    Console.WriteLine("Integration test completed.");
}
catch (Exception ex)
{
    Console.WriteLine("ERROR: " + ex);
}

record AuthResult(bool Success, string Message, string? Token, string? UserName);
