using ClientHealthMonitor.Data;
using ClientHealthMonitor.Data.Domain;
using ClientHealthMonitor.Models;
using ClientHealthMonitorApi.Models;
using CommonLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

namespace ClientHealthMonitor.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ClientController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ClientController(AppDbContext context)
        {
            _context = context;
        }

        private Guid GetUserId()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.Parse(userId!);
        }

        // Create Client
        [HttpPost]
        public async Task<IActionResult> CreateClient([FromBody] ClientCreateRequest request)
        {
            var userId = GetUserId();

            var client = new Clients
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Client_Name = request.Name,
                Client_Email = request.Email,
                Phone = request.Phone,
                Notes = request.Notes,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Last_Contact_At = DateTimeOffset.UtcNow
            };

            _context.Clients.Add(client);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetClientDetails), new { clientId = client.Id }, client);
        }

        // List Clients
        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            var userId = GetUserId();
            var clients = await _context.Clients
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(clients);
        }


        // Get Client Details
        [HttpGet("{clientId:guid}")]
        public async Task<IActionResult> GetClientDetails(Guid clientId)
        {
            var userId = GetUserId();

            var client = await _context.Clients
                .Where(c => c.Id == clientId && c.UserId == userId)
                .Select(c => new
                {
                    // Core Identity
                    Id = c.Id,
                    Email = c.Client_Email,
                    Name = c.Client_Name,
                    Phone = c.Phone,
                    Notes = c.Notes,
                
                    // Status & Stages
                    LastContactAt = c.Last_Contact_At,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                
                    // AI Score & Insights (Null-safe)
                    HealthScore = c.Score != null ? (decimal?)c.Score.Score : null,
                    //RiskLevel = c.Score != null ? c.Score.RiskLevel : null,
                    //Confidence = c.Score != null ? c.Score.Confidence : 0,
                    Summary = c.Score != null ? c.Score.Summary : null,
                    CalculatedAt = c.Score != null ? (DateTimeOffset?)c.Score.CalculatedAt : null,
                
                    // Metrics
                    InteractionCount = c.Interactions.Count()
                })
                .FirstOrDefaultAsync();

            if (client == null)
                return NotFound();

            return Ok(client);
        }

        //Update Client
        [HttpPut("{clientId:guid}")]
        public async Task<IActionResult> UpdateClient(Guid clientId, [FromBody] ClientUpdateRequest request)
        {
            var userId = GetUserId();
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.UserId == userId);
            if (client == null)
                return NotFound();

            client.Client_Name = request.Name ?? client.Client_Name;
            client.Client_Name = request.Email ?? client.Client_Name;
            client.Phone = request.Phone ?? client.Phone;
            client.Notes = request.Notes ?? client.Notes;
            client.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(client);
        }

        //// Delete Client
        [HttpDelete("{clientId:guid}")]
        public async Task<IActionResult> DeleteClient(Guid clientId)
        {
            var userId = GetUserId();

            // 1. Use a transaction to ensure both Client and ClientScore are deleted together
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {                
                var client = await _context.Clients
                    .Include(c => c.Score)
                    .Include(c => c.Interactions)
                    .FirstOrDefaultAsync(c => c.Id == clientId && c.UserId == userId);

                if (client == null)
                {
                    return NotFound(new { message = "Client not found or unauthorized." });
                }

                
                if (client.Score != null)
                {
                    _context.ClientScores.Remove(client.Score);
                }

                if (client.Interactions != null && client.Interactions.Any())
                {
                    _context.ClientInteractions.RemoveRange(client.Interactions);
                }              

                _context.Clients.Remove(client);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return NoContent(); // 204 No Content is standard for successful deletes
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, "An error occurred while deleting the client.");
            }
        }


        [HttpGet("healthscore")]
        public async Task<IActionResult> GetClientsHealtScore()
        {
            var clients = await _context.ClientScores
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(clients);
        }

        // Get Each Client Health Score (route changed to avoid conflict)
        [HttpGet("{clientId:guid}/healthscore")]
        public async Task<IActionResult> GetEachClientHealthScore(Guid clientId)
        {
            var clientScore = await _context.ClientScores
                .FirstOrDefaultAsync(c => c.ClientId == clientId);

            if (clientScore == null)
                return NotFound();

            return Ok(clientScore);
        }


        [HttpGet("dashboard")]
        public async Task<ActionResult<IEnumerable<ClientCardUiModel>>> GetClientsForDashBoard()
        {
           
            var userId = GetUserId();

            var clients = await _context.Clients
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.Score!.Score) // Sorts by the Score property in ClientScore
                .Take(5)                               // Limits result to top 5
                .ToListAsync();

            var clientIds = clients.Select(c => c.Id).ToList();

            // Get latest score for each client
            var latestScores = await _context.ClientScores
                .Where(cs => clientIds.Contains(cs.ClientId))
                .GroupBy(cs => cs.ClientId)
                .Select(g => g.OrderByDescending(x => x.CalculatedAt).First())
                .ToListAsync();

            var scoreLookup = latestScores.ToDictionary(s => s.ClientId);

            var clientCardUiModels = new List<ClientCardUiModel>();

            foreach (var client in clients)
            {
                if (!scoreLookup.TryGetValue(client.Id, out var score))
                    continue;

                var healthResult = new ClientHealthScoreResult
                {
                    Score = score.Score,
                    RelationshipHealth = score.RelationshipHealth,
                    ExecutionHealth = score.ExecutionHealth,
                    OpportunityBoost = score.OpportunityBoost,
                    DataCoverage = score.DataCoverage,
                    Tier = score.Tier
                };

                var uiModel = ClientHealthUiMapper.ToClientCardUi(
                    healthResult,
                    Segment.Freelancer
                );

                uiModel.Id = client.Id;
                uiModel.Name = client.Client_Name;
                uiModel.Email = client.Client_Email;

                clientCardUiModels.Add(uiModel);
            }

            return Ok(clientCardUiModels);
        }
    }
}
