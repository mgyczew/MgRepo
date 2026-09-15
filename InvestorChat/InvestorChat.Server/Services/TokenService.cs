using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InvestorChat.Server.Models;
using Microsoft.IdentityModel.Tokens;

namespace InvestorChat.Server.Services;

public class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(UserAccount user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? "SuperSecretKeyForInvestorChat1234567890";
        var issuer = _configuration["Jwt:Issuer"] ?? "InvestorChat";
        var audience = _configuration["Jwt:Audience"] ?? "InvestorChatUsers";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new("username", user.UserName),
            new("userId", user.Id.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
