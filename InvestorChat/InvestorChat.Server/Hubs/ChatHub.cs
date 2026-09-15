using System.Security.Claims;
using InvestorChat.Server.Data;
using InvestorChat.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace InvestorChat.Server.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _db;

    public ChatHub(AppDbContext db)
    {
        _db = db;
    }

    public async Task SendMessage(string text)
    {
        var userName = Context.User?.Identity?.Name ?? Context.User?.FindFirst("username")?.Value ?? "Anonymous";
        var trimmed = text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var message = new ChatMessage
        {
            UserName = userName,
            Content = trimmed,
            CreatedAt = DateTime.UtcNow
        };

        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();

        await Clients.All.SendAsync("ReceiveMessage", userName, message.Content, message.CreatedAt.ToString("HH:mm:ss"));
    }

    public async Task<List<ChatMessage>> GetRecentMessages()
    {
        return await _db.ChatMessages
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }
}
