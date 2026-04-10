using ClientHealthMonitor.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
namespace ClientHealthMonitor.Services
{
    public interface IJwtTokenService
    {
        string CreateToken(GoogleUser user);
    }
    public class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration config;
        public JwtTokenService(IConfiguration cfg) => config = cfg;

        public string CreateToken(GoogleUser user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
            new Claim("userRole", user.UserRole ?? ""),
            new Claim("google_sub", user.Sub ?? string.Empty)
        };

            var token = new JwtSecurityToken(
                issuer: config["JwtSettings:Issuer"],
                audience: config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(int.Parse(config["JwtSettings:ExpiryMinutes"] ?? "60")),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}
