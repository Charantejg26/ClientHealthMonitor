using ClientHealthMonitor.Data;
using ClientHealthMonitor.Models;
using ClientHealthMonitor.Services;
using ClientHealthMonitor.Services.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace ClientHealthMonitor.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly IGoogleAuthService _google;
        private readonly IJwtTokenService _jwtService;
        private readonly AppDbContext _context;
        public AuthController(IGoogleAuthService google, ILogger<AuthController> logger, IJwtTokenService jwtService, AppDbContext context)
        {
            _google = google;
            _logger = logger;
            _jwtService = jwtService;
            _context = context;
        }

        [HttpPost("GetGoogleUser")]
        public async Task<ActionResult<GoogleUser>> GetGoogleUserInfoAsync([FromBody] GoogleAuthRequest request)
        {

            // Enforce HTTPS
            if (!Request.IsHttps)
            {
                _logger.LogWarning("Non-HTTPS request blocked.");
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "HTTPS is required." });
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 512)
                return BadRequest(new { message = "Invalid or missing code." });

            try
            {

                var exchange = await _google.ExchangeCodeAsync(request.Code);
                if (!exchange.Success)
                    return BadRequest(new { message = "Token exchange failed", details = exchange.error });

                if (string.IsNullOrEmpty(exchange.access_token))
                    return BadRequest(new { message = "No access token from Google" });

                // Get user info
                var gUser = await _google.GetUserInfoAsync(exchange);
                if (gUser == null)
                    return BadRequest(new { message = "Failed to get user info from Google" });

                var jwtToken = _jwtService.CreateToken(gUser);

                // Set the secure HttpOnly cookie
                Response.Cookies.Append("X-Access-Token", jwtToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true, // Always true in production (HTTPS)
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTime.UtcNow.AddDays(7)
                });

                return Ok(new
                {
                    gUser
                    //token = jwtToken
                });
            }
            catch (InvalidJwtException ex)
            {
                // Handle invalid token (e.g., token has expired or is not from Google)               
                return BadRequest(new { message = "Invalid ID Token." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetGoogleUserInfoAsync.");
                // Do not leak stack trace or internal error details
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Internal server error." });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("X-Access-Token");
            return Ok();
        }


        [Authorize]
        [HttpGet("me")]
        public IActionResult GetCurrentUser()
        {
            // .NET automatically parses the Cookie/JWT into the 'User' object
            // using the 'OnMessageReceived' event we set up in Program.cs
            var user = new
            {
                Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Email = User.FindFirst(ClaimTypes.Email)?.Value,
                Name = User.FindFirst("name")?.Value,
                UserRole = User.FindFirst("userRole")?.Value
            };

            return Ok(user);
        }


        [Authorize]
        [HttpPost("sendemail")]
        public async Task<IActionResult> SendEmail([FromBody] EmailRequest request)
        {
            // Get the Access Token from your DB for the current user
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized("User ID not found in token.");
            }

            var tokenRecord = await _context.AuthTokens.FirstOrDefaultAsync(t => t.UserId == Guid.Parse(userIdString));

            if (tokenRecord == null || string.IsNullOrEmpty(tokenRecord.AccessToken))
            {
                return BadRequest("No Google account linked or token missing.");
            }

            // 3. Pass to service to handle the logic
            var success = await _google.SendEmailAsync(tokenRecord.UserId, tokenRecord.RefreshToken, tokenRecord.AccessToken, tokenRecord.AccessToken_ExpiresAt, request);

            if (!success) return StatusCode(500, "Failed to send email via Google.");

            return Ok(new { message = "Email sent successfully!" });


        }

        [Authorize]
        [HttpPost("setuserrole")]
        public async Task<IActionResult> SetSegment([FromBody] SetUserRoleRequest request)
        {
            // Get the User ID from the Auth Token (Claims)
            
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized("User ID not found in token.");
            }

            // Find the user in the database
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == Guid.Parse(userIdClaim));

            if (user == null)
            {
                return NotFound("User not found.");
            }

            // Update the UserType (segment)
            user.UserRole = request.UserRole;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            // Save changes
            try
            {
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                // Log the error
                return StatusCode(500);
            }
        }
    }
}
