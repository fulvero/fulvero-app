using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fulvero.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Fulvero.Api.Security;

public class JwtTokenService(IConfiguration configuration)
{
    public string CreateToken(
        AppUser user,
        Guid? effectiveCompanyId = null,
        string? effectiveCompanyName = null,
        Guid? homeCompanyId = null,
        string? homeCompanyName = null)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var companyId = effectiveCompanyId ?? user.CompanyId;
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("company_id", companyId.ToString()),
            new Claim("display_name", user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role)
        };

        if (effectiveCompanyId is not null && effectiveCompanyId.Value != user.CompanyId)
        {
            claims.Add(new Claim("is_impersonating", "true"));
            claims.Add(new Claim("home_company_id", (homeCompanyId ?? user.CompanyId).ToString()));
            claims.Add(new Claim("home_company_name", homeCompanyName ?? user.Company?.Name ?? string.Empty));
            claims.Add(new Claim("impersonated_company_id", effectiveCompanyId.Value.ToString()));
            claims.Add(new Claim("impersonated_company_name", effectiveCompanyName ?? string.Empty));
        }

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
