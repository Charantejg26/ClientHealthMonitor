using ClientHealthMonitor.Data;
using ClientHealthMonitor.Data.Domain;
using ClientHealthMonitor.Models;
using ClientHealthMonitor.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

//namespace ClientHealthMonitor.Controllers
//{
//    [ApiController]
//    [Route("clients/{clientId:guid}/interactions")]
//    [Authorize]
//    public class ClientInteractionsController : ControllerBase
//    {
//        private readonly AppDbContext _context;
//        private readonly IFileStorageService _storage;

//        public ClientInteractionsController(AppDbContext context, IFileStorageService storage)
//        {
//            _context = context;
//            _storage = storage;
//        }

//        [HttpPost]
//        public async Task<IActionResult> CreateInteraction(Guid clientId, [FromForm] CreateInteractionRequest request)
//        {
//            var client = await _context.Clients.FindAsync(clientId);
//            if (client == null)
//                return NotFound("Client not found.");

//            var userId = GetUserId();

//            string? uploadedFileKey = null;

//            if (request.File != null)
//            {
//                // Validate file
//                if (request.File.Length == 0)
//                    return BadRequest("Uploaded file is empty.");

//                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".heic", ".txt", ".docx" };
//                var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();

//                if (!allowedExtensions.Contains(extension))
//                    return BadRequest($"File type '{extension}' is not supported. Allowed: {string.Join(", ", allowedExtensions)}");

//                if (request.File.Length > 10 * 1024 * 1024) // 10 MB limit
//                    return BadRequest("File too large. Maximum allowed size is 10 MB.");

//                //Upload file to AWS S3
//                uploadedFileKey = await _storage.UploadFileAsync(request.File, clientId.ToString());
//            }

//            var interaction = new ClientInteraction
//            {
//                Id = Guid.NewGuid(),
//                ClientId = clientId,
//                Client = client,
//                UserId = userId,
//                User = await _context.Users.FindAsync(userId) ?? throw new Exception("User not found."),
//                InteractionType = request.InteractionType,
//                //Content = request.Content ?? uploadedFileKey ?? "(empty)",
//                InteractionOccurredAt = request.InteractionOccurredAt ?? DateTimeOffset.UtcNow,
//                CreatedAt = DateTimeOffset.UtcNow,
//                UpdatedAt = DateTimeOffset.UtcNow,

//            };

//            _context.ClientInteractions.Add(interaction);
//            await _context.SaveChangesAsync();

//            return CreatedAtAction(nameof(GetInteraction), new { clientId, id = interaction.Id }, interaction);
//        }

//        // Get all interactions for a client
//        [HttpGet]
//        public async Task<IActionResult> GetAllInteractions(Guid clientId)
//        {
//            var interactions = await _context.ClientInteractions
//                .Include(i => i.User)
//                .Where(i => i.ClientId == clientId)
//                .OrderByDescending(i => i.InteractionOccurredAt)
//                .ToListAsync();

//            return Ok(interactions);
//        }

//        //Get a specific interaction
//        [HttpGet("{id:guid}")]
//        public async Task<IActionResult> GetInteraction(Guid clientId, Guid id)
//        {
//            var interaction = await _context.ClientInteractions
//                .Include(i => i.User)
//                .FirstOrDefaultAsync(i => i.ClientId == clientId && i.Id == id);

//            if (interaction == null)
//                return NotFound();

//            return Ok(interaction);
//        }

//        //Update an existing interaction (edit content, type, etc.)
//        [HttpPut("{id:guid}")]
//        public async Task<IActionResult> UpdateInteraction(Guid clientId, Guid id, [FromBody] UpdateInteractionRequest request)
//        {
//            var interaction = await _context.ClientInteractions
//                .Include(i => i.User)
//                .FirstOrDefaultAsync(i => i.ClientId == clientId && i.Id == id);

//            if (interaction == null)
//                return NotFound();

//            interaction.InteractionType = request.InteractionType ?? interaction.InteractionType;
//            //interaction.Content = request.Content ?? interaction.Content;
//            interaction.InteractionOccurredAt = request.InteractionOccurredAt ?? interaction.InteractionOccurredAt;
//            interaction.UpdatedAt = DateTimeOffset.UtcNow;

//            await _context.SaveChangesAsync();

//            return Ok(interaction);
//        }

//        // Delete an interaction
//        [HttpDelete("{id:guid}")]
//        public async Task<IActionResult> DeleteInteraction(Guid clientId, Guid id)
//        {
//            var interaction = await _context.ClientInteractions
//                .FirstOrDefaultAsync(i => i.ClientId == clientId && i.Id == id);

//            if (interaction == null)
//                return NotFound();

//            // if Content contains S3 file key, delete it
//            //if (interaction.Content != null && interaction.Content.Contains("/"))
//            //{
//            //    await _storage.DeleteFileAsync(interaction.Content);
//            //}

//            _context.ClientInteractions.Remove(interaction);
//            await _context.SaveChangesAsync();

//            return Ok();
//        }

//        // Helper to get logged-in user ID from JWT
//        private Guid GetUserId()
//        {
//            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//            return Guid.Parse(userId!);
//        }
//    }
//}
