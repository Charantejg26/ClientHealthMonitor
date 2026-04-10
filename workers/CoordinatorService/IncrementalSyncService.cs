using Amazon.SQS;
using Amazon.SQS.Model;
using CommonLib;
using Dapper;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoordinatorService
{
    public class IncrementalSyncService : BackgroundService
    {
        private readonly ILogger<IncrementalSyncService> _log;
        private readonly IAmazonSQS _sqs;
        private readonly string _queueUrl;
        private readonly string _connString;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly GoogleApiTokenService _tokenService;

        public IncrementalSyncService(ILogger<IncrementalSyncService> log, IAmazonSQS sqs, IConfiguration config,
            ITokenEncryptionService cryptoService, GoogleApiTokenService apiTokenService)
        {
            _log = log; _sqs = sqs;
            _queueUrl = config["AWS:SQS_QUEUE_URL"] ?? "";
            _connString = config.GetConnectionString("PostgreConnection") ?? "";
            _clientId = config["GOOGLE_CLIENT_ID"] ?? "";
            _clientSecret = config["GOOGLE_CLIENT_SECRET"] ?? "";
            _tokenService = apiTokenService;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var users = await GetUsersWithHistoryIdAsync();

                    foreach (var user in users)
                    {
                        try
                        {
                            await RunIncrementalSyncForUser(user.UserId, user.Email, user.RefreshToken, user.AccessToken, user.TokenExpiry, user.HistoryId, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Incremental sync failed for {UserId}", user.UserId);
                        }
                    }

                    if (users.Count() == 0)
                    {
                        _log.LogInformation("No users with history ID found for incremental sync. Exiting loop.");
                        break;
                    }
                    
                    
                }
                catch (NpgsqlException ex) when (ex.InnerException is TimeoutException || ex.IsTransient)
                {
                    // Handle Database Timeouts gracefully
                    _log.LogWarning("Database timeout/busy. Waiting 3 minutes before retrying...");

                    // This is the "Backoff" logic you requested
                    await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
                }
                catch (Exception ex)
                {
                    // Handle other unexpected errors
                    _log.LogError(ex, "Unexpected error in IncrementalSyncService. Retrying in 1 minute.");
                    //await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }

            }
        }

       

        private async Task<IEnumerable<(Guid UserId, string Email, string RefreshToken, string AccessToken, DateTimeOffset TokenExpiry, string HistoryId)>> GetUsersWithHistoryIdAsync()
        {
            try
            {
                using var conn = new NpgsqlConnection(_connString);
                var sql = @"SELECT u.id as UserId, u.Email as Email, t.RefreshToken,t.AccessToken, t.AccessToken_ExpiresAt as TokenExpiry, t.gmail_last_history_id as HistoryId
                     FROM users u
                     JOIN authTokens t on t.userid = u.id
                     WHERE t.gmail_last_history_id IS NOT NULL;";
                return await conn.QueryAsync<(Guid, string, string, string, DateTimeOffset, string)>(sql);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in Getting USers with History Id");
            }
            return Enumerable.Empty<(Guid, string, string, string, DateTimeOffset, string)>();
        }

        private async Task RunIncrementalSyncForUser(Guid userId, string userEmail, string refreshToken, string accessTokenDb, DateTimeOffset tokenExpiry, string historyId, CancellationToken ct)
        {
            var accessToken = await _tokenService.EnsureAccessToken(userId, _clientId, _clientSecret, refreshToken, accessTokenDb, tokenExpiry);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _log.LogError("Failed to obtain access token in Incremenatl service for user {UserId}", userId);
                return;
            }

            var credential = GoogleCredential.FromAccessToken(accessToken);
            var svc = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "ClientHealthScore" });

            var req = svc.Users.History.List("me");
            req.StartHistoryId = ulong.TryParse(historyId, out var val) ? val : (ulong?)null;            

            ListHistoryResponse resp;

            try
            {
                resp = await req.ExecuteAsync(ct);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // History expired → force full re-sync
                await ResetHistoryId(userId);
                return;
            }

            if (resp.History != null)
            {
                foreach (var h in resp.History)
                {
                    if (h.MessagesAdded != null)
                    {
                        foreach (var m in h.MessagesAdded)
                            await EnqueueMessage(userId, m.Message.Id);
                    }

                    if (h.MessagesDeleted != null)
                    {
                        foreach (var m in h.MessagesDeleted)
                            await DeleteInteraction(userId, m.Message.Id);
                    }
                }
            }

            if(resp.HistoryId.ToString() != historyId)
                await SaveLastHistoryId(userId, resp.HistoryId.ToString());
        }

        private async Task ResetHistoryId(Guid userId)
        {
            using var conn = new NpgsqlConnection(_connString);

            await conn.ExecuteAsync(@"UPDATE authTokens
             SET  gmail_last_history_id = NULL,
            WHERE userid = @uid", new { uid = userId });

            await conn.ExecuteAsync(@"UPDATE users
             SET  last_initial_sync_started_at = NULL,
            WHERE id = @uid", new { uid = userId });

            _log.LogWarning(
                "Gmail history expired for user {UserId}. Marked for full backfill.",
                userId
            );
        }

        private async Task EnqueueMessage(Guid userId, string gmailMessageId)
        {
            var payload = new
            {
                UserId = userId,
                GmailMessageId = gmailMessageId
            };

            var request = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = JsonSerializer.Serialize(payload)
            };

            await _sqs.SendMessageAsync(request);
        }

        private async Task DeleteInteraction(Guid userId, string gmailMessageId)
        {
            using var conn = new NpgsqlConnection(_connString);

            await conn.ExecuteAsync(@"
            DELETE FROM clientInteractions
            WHERE UserId = @uid
            AND gmail_message_id = @gid",
            new
            {
                uid = userId,
                gid = gmailMessageId
            });
        }


        private async Task SaveLastHistoryId(Guid userId, string? historyId)
        {
            using var conn = new NpgsqlConnection(_connString);

            await conn.ExecuteAsync(@"UPDATE authTokens
            SET
            gmail_last_history_id = @hid,
            UpdatedAt = now()
            WHERE userid = @uid
            ", new
            {
                uid = userId,
                hid = historyId
            });
        }


    }
}
