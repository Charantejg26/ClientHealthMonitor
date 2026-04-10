using Amazon.SQS;
using Amazon.SQS.Model;
using CommonLib;
using Dapper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Newtonsoft.Json.Linq;
using Npgsql;
using System.Text.Json;


namespace CoordinatorService
{
    public class CoordinatorService : BackgroundService
    {
        private readonly ILogger<CoordinatorService> _log;
        private readonly IAmazonSQS _sqs;
        private readonly string _queueUrl;
        private readonly string _connString; // Postgres connection string
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly GoogleApiTokenService _tokenService;

        public CoordinatorService(ILogger<CoordinatorService> log, IAmazonSQS sqs, IConfiguration config,
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
            // Example: single run loop; in prod trigger per user or batch via EventBridge
            while (!stoppingToken.IsCancellationRequested)
            {
                var usersToSync = await GetUsersToInitialSyncAsync();
                foreach (var user in usersToSync)
                {
                    try
                    {
                        await RunInitialSyncForUser(user.UserId, user.Email, user.RefreshToken, user.AccessToken, user.TokenExpiry, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Initial sync failed for user {UserId}", user.UserId);
                    }
                }

               if(usersToSync.Count() == 0)
               {
                    _log.LogInformation("No users to sync. exit the loop...");
                    break;
               }
            }
        }


        private async Task<IEnumerable<(Guid UserId, string Email, string RefreshToken, string AccessToken, DateTimeOffset TokenExpiry)>> GetUsersToInitialSyncAsync()
        {
            try
            {
                using var conn = new NpgsqlConnection(_connString);
                var sql = @"SELECT u.id as UserId, u.Email as Email, t.RefreshToken,t.AccessToken, t.AccessToken_ExpiresAt as TokenExpiry
                     FROM users u
                     JOIN authTokens t on t.userid = u.id
                     WHERE t.last_initial_sync_started_at IS NULL OR (t.last_initial_sync_started_at < now() - interval '30 days')
                     LIMIT 100"; // batch size
                return await conn.QueryAsync<(Guid, string, string, string, DateTimeOffset)>(sql);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Database timeout or error in CoordinatorService");
                return Enumerable.Empty<(Guid, string, string, string, DateTimeOffset)>();
            }

        }


        private async Task RunInitialSyncForUser(Guid userId, string userEmail, string refreshToken, string accessTokenDb, DateTimeOffset tokenExpiry, CancellationToken ct)
        {
            

            // Use stored refresh token to get access token — user says tokens already obtained, so maybe have access_token
            var accessToken = await _tokenService.EnsureAccessToken(userId, _clientId, _clientSecret, refreshToken, accessTokenDb, tokenExpiry);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _log.LogError("Failed to obtain access token in Coordinator service for user {UserId}", userId);
                return;
            }

            var credential = GoogleCredential.FromAccessToken(accessToken);
            var svc = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "ClientHealthScore" });

            string pageToken = null;
            do
            {
                var req = svc.Users.Messages.List("me");
                // Use Gmail's search query to drastically reduce irrelevant results before fetching details:
                // Prioritize personal/primary categories, exclude automated, social, and promotional noise.
                //newer_than:90d to limit to recent emails only.
                req.Q = "category:(personal OR primary) -label:notifications -category:social -category:promotions -list:unsubscribe newer_than:90d";
                req.IncludeSpamTrash = false;               
                req.MaxResults = 500; // max allowed
                req.PageToken = pageToken;

                var resp = await req.ExecuteAsync(ct);
                if (resp.Messages != null && resp.Messages.Count > 0)
                {
                    var batches = resp.Messages.Select(m => m.Id).Chunk(10); // batch of 10 to SQS
                    foreach (var batch in batches)
                    {
                        var entries = batch.Select((id, idx) => new SendMessageBatchRequestEntry
                        {
                            Id = idx.ToString(),
                            MessageBody = JsonSerializer.Serialize(new SyncMessageId(userId, id))
                        }).ToList();

                        var sendReq = new SendMessageBatchRequest(_queueUrl, entries);
                        var sendResp = await _sqs.SendMessageBatchAsync(sendReq, ct);

                        if (sendResp.HttpStatusCode != System.Net.HttpStatusCode.OK)
                        {
                            if (sendResp.Failed != null && sendResp.Failed.Count > 0)
                            {
                                _log.LogWarning("{Count} messages failed to enqueue for user {User}", sendResp.Failed.Count, userId);
                            }
                            else
                            {
                                _log.LogError("Failed to enqueue messages for user {User}", userId);
                            }

                        }
                    }
                    
                }               

                pageToken = resp.NextPageToken;               


            } while (!string.IsNullOrEmpty(pageToken) && !ct.IsCancellationRequested);


            var profile = await svc.Users.GetProfile("me").ExecuteAsync(ct);

            var lastHistoryId = profile.HistoryId.ToString();

            if (string.IsNullOrEmpty(lastHistoryId))
            {
                throw new InvalidOperationException("Gmail profile did not return HistoryId");
            }

            // After enqueuing all messageIds, update last_initial_sync_started_at
            using var conn = new NpgsqlConnection(_connString);
            await conn.ExecuteAsync("UPDATE authTokens SET last_initial_sync_started_at = now(), gmail_last_history_id = @hid" +
                "  WHERE UserId = @uid", new { uid = userId, hid = lastHistoryId });
        }

    }
}
