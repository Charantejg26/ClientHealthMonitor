using Amazon.SQS;
using Amazon.SQS.Model;
using CommonLib;
using Dapper;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Npgsql;
using ProcessorService.Utilities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Dapper.SqlMapper;
using GmailHeader = Google.Apis.Gmail.v1.Data.MessagePartHeader;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;
using GmailMessagePart = Google.Apis.Gmail.v1.Data.MessagePart;

namespace ProcessorService
{
    public class ProcessorService : BackgroundService
    {
        private readonly ILogger<ProcessorService> _log;
        private readonly IAmazonSQS _sqs;
        private readonly string _queueUrl;
        private readonly string _scoreQueueUrl;
        private readonly string _connString;
        private readonly int _maxMessagesPerReceive = 10;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly GoogleApiTokenService _tokenService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GmailIngestionOptions _options;

        public ProcessorService(ILogger<ProcessorService> log, IAmazonSQS sqs,
            IConfiguration config, GoogleApiTokenService apiTokenService, IServiceScopeFactory scopeFactory)
        {
            _log = log; _sqs = sqs;
            _queueUrl = config["AWS:SQS_QUEUE_URL"] ?? "";
            _scoreQueueUrl = config["AWS:SQS_SCORE_QUEUE_URL"] ?? "";
            _connString = config.GetConnectionString("PostgreConnection") ?? "";
            _clientId = config["GOOGLE_CLIENT_ID"] ?? "";
            _clientSecret = config["GOOGLE_CLIENT_SECRET"] ?? "";
            _tokenService = apiTokenService;
            _scopeFactory = scopeFactory;
            _options = config.GetSection("GmailIngestion").Get<GmailIngestionOptions>() ?? new GmailIngestionOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _log.LogInformation("Processor worker started");

            await RunEmailQueueAsync(stoppingToken);
            await RunScoreQueueAsync(stoppingToken);
        }

        private async Task RunEmailQueueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var resp = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _maxMessagesPerReceive,
                    WaitTimeSeconds = 20,
                    VisibilityTimeout = 120
                }, ct);

                // If no messages are returned, the queue is "empty" for now.
                if (resp.Messages == null || resp.Messages.Count == 0)
                {
                    _log.LogInformation("Email Queue is empty. Moving to next task.");
                    break;
                }

                // use this for debugging single message processing
                //await ProcessSqsMessageAsync(resp.Messages[0], ct);
                var tasks = resp.Messages?.Select(m => ProcessSqsMessageAsync(m, ct)) ?? Enumerable.Empty<Task>();
                await Task.WhenAll(tasks);
            }
        }

        private async Task RunScoreQueueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var resp = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _scoreQueueUrl,
                    MaxNumberOfMessages = 1, // FIFO: one at a time per group
                    WaitTimeSeconds = 20,
                    VisibilityTimeout = 300
                }, ct);

                if (resp.Messages == null || resp.Messages.Count == 0)
                {
                    break;
                }

                foreach (var msg in resp.Messages ?? Enumerable.Empty<Message>())
                {
                    await ProcessScoreMessageAsync(msg, ct);
                }
            }
        }



        private async Task ProcessSqsMessageAsync(Message sqsMessage, CancellationToken ct)
        {
            SyncMessageId payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<SyncMessageId>(sqsMessage.Body) ?? throw new InvalidOperationException("Failed to deserialize SyncMessageId from SQS message body.");
                // Load access token for user from DB
                await using var conn = new NpgsqlConnection(_connString);
                await conn.OpenAsync(ct);

                (Guid UserId, string UserEmail, string UserRole, string AccessToken, string RefreshToken, DateTimeOffset TokenExpiry)? tokenRow = await GetUserAndTokensAsync(conn, payload.UserId);

                if (tokenRow == null) throw new Exception("No token for user");


                var (userId, userEmail, userRole, accessTokenDb, refreshToken, tokenExpiry) = tokenRow.Value;
                var accessToken = await _tokenService.EnsureAccessToken(userId, _clientId, _clientSecret, refreshToken, accessTokenDb, tokenExpiry);
                // If expired, refresh — omitted here, see Coordinator.EnsureAccessToken for example

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _log.LogError("Failed to obtain access token for user {UserId}", payload.UserId);
                    return;
                }

                //var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromAccessToken(accessToken);
                //var svc = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "ClientHealthScore" });
                var svc = CreateGmailService(accessToken);

                // Idempotency short-circuit: if already ingested, ack and stop.
                if (await InteractionExistsAsync(conn, payload.UserId, payload.GmailMessageId, ct))
                {
                    await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);
                    return;
                }

                // 1) Metadata-first fetch (fast + low exposure)
                var metaMsg = await GetMessageMetadataAsync(svc, payload.GmailMessageId, ct);
                if (metaMsg == null)
                {
                    await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);
                    return;
                }

                var headers = ExtractHeaders(metaMsg);
                if (headers.Count == 0)
                {
                    _log.LogWarning("No headers found for message {MessageId}", payload.GmailMessageId);
                    await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);
                    return;
                }

                //var getReq = svc.Users.Messages.Get("me", payload.GmailMessageId);
                //getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                //getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                //getReq.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(new[] { "From", "To", "Cc", "Date", "Subject", "Message-Id", "In-Reply-To", "References" });

                // Correct skip logic: if it's automated/system email => skip ingestion
                if (IsAutomatedSystemEmail(headers))
                {
                    _log.LogInformation("Skipping automated/system email {MessageId}", payload.GmailMessageId);
                    await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);
                    return;
                }


                var parsed = EmailHeaderParser.Parse(headers, metaMsg.InternalDate); // RFC-5322 robust parser in Common   

                bool isOutgoing = EmailUtils.IsOutgoing(parsed.From, userEmail);
                var clientEmail = EmailUtils.DetermineClientEmail(isOutgoing, parsed, userEmail);
                var snippet = TextCleaner.CleanSnippet(metaMsg.Snippet ?? "");

                // 2) Decide whether to fetch body (policy)
                //var decision = DecideIngestion(parsed.Subject, snippet, headers);

                // Upsert client
                var clientId = await UpsertClientAsync(conn, payload.UserId, clientEmail, parsed);


                // 4) Fetch body if needed (FULL) and extract best content
                //string? emailBody = null;
                //string emailType = "none";

                //if (!_options.AlwaysMetadataOnly && decision.BodyDecision != BodyFetchDecision.Never)
                //{
                //    var fullMsg = await GetMessageFullAsync(svc, payload.GmailMessageId, ct);
                //    if (fullMsg != null)
                //    {
                //        var bodyResult = GmailBodyExtractor.ExtractBestBody(fullMsg);
                //        emailBody = bodyResult.Body;
                //        emailType = bodyResult.MimeType;

                //        if (!_options.StoreEmailBodies || decision.BodyDecision == BodyFetchDecision.FetchEphemeral)
                //        {
                //            // Privacy-first: don't store full body.
                //            emailBody = null;
                //            emailType = "none";
                //        }
                //        else
                //        {
                //            // Store with safety cap
                //            emailBody = Abbreviate(emailBody, _options.MaxBodyCharsToStore);
                //        }
                //    }
                //}

                // Upsert client_interactions
                await UpsertInteractionAsync(conn, payload.UserId, clientId, metaMsg.Id, metaMsg.ThreadId, isOutgoing, snippet, parsed);

                // Debounce enqueue recompute job (e.g., add to another queue with small debounce)
                await EnqueueRecomputeAsync(clientId, userRole);

                // Delete SQS message after success
                await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle, ct);

            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _log.LogWarning(gex, "Message {Id} not found (maybe deleted)", payload?.GmailMessageId);
                await _sqs.DeleteMessageAsync(_queueUrl, sqsMessage.ReceiptHandle);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Let SQS retry naturally (do not delete).
                _log.LogWarning("Processing cancelled for message {Body}", sqsMessage.Body);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Processing SQS message failed for {Body}", sqsMessage.Body);
                // Let message become visible again; after maxReceiveCount it will land in DLQ.
            }
        }


        private async Task ProcessScoreMessageAsync(Message sqsMessage, CancellationToken ct)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<RecomputeMessage>(sqsMessage.Body);

                if (payload == null)
                    throw new Exception("Invalid message payload");

                var clientId = payload.ClientId;
                if (!Enum.TryParse<Segment>(payload.UserRole, ignoreCase: true, out var segment))
                {
                    throw new ArgumentException($"Invalid user role: {payload.UserRole}");
                }

                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    var calculator = scope.ServiceProvider.GetRequiredService<HealthScoreCalculatorService>();
                    await calculator.CalculateAndPersistScoreAsync(clientId, segment, ct); // segment needs to come from frontend
                }

                await _sqs.DeleteMessageAsync(_scoreQueueUrl, sqsMessage.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Score recompute failed");
                // retry via SQS
            }
        }


        private async Task<Guid> UpsertClientAsync(NpgsqlConnection conn, Guid userId, string clientEmail, dynamic parsed)
        {
            var sql = @"
                INSERT INTO clients (Id, UserId, Client_Email, Client_Name, Last_Contact_At, CreatedAt, UpdatedAt )
                VALUES (@Id, @UserId, @Email, @DisplayName, @LastContact, @CreatedAt, @UpdatedAt)
                ON CONFLICT (UserId, Client_Email) DO UPDATE SET Last_Contact_At = GREATEST(clients.Last_Contact_At, EXCLUDED.Last_Contact_At), UpdatedAt = EXCLUDED.UpdatedAt
                RETURNING id;";

            var clientID = Guid.NewGuid();
            var lastContact = parsed.Date ?? DateTime.UtcNow;
            var id = await conn.QuerySingleAsync<Guid>(sql, new
            {
                Id = clientID,
                UserId = userId,
                Email = clientEmail.ToLowerInvariant(),
                DisplayName = parsed.From?.Item2 ?? "",
                LastContact = lastContact,
                //EngagementStage = "Lead", // default status
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return id;
        }

        private async Task UpsertInteractionAsync(NpgsqlConnection conn, Guid userId, Guid clientId, string gmailMessageId, string threadId, bool isOutgoing, string snippet, dynamic parsed)
        {
            var sql = @"
                INSERT INTO clientInteractions (Id, UserId, ClientId, InteractionType, IsOutgoing, gmail_message_id, gmail_thread_id, snippet, subject, message_date, raw_headers, parsed_headers, CreatedAt, UpdatedAt )
                VALUES (@Id, @UserId, @ClientId, @InteractionType, @IsOutgoing, @GmailId, @ThreadId,  @Snippet, @Subject, @MessageDate, @Raw, @Parsed, @CreatedAt, @UpdatedAt )
                ON CONFLICT (UserId, gmail_message_id) DO UPDATE SET
                  snippet = EXCLUDED.snippet,
                  subject = EXCLUDED.subject,
                  message_date = EXCLUDED.message_date,
                  parsed_headers = EXCLUDED.parsed_headers,
                  UpdatedAt = now();";

            var clientInteractionId = Guid.NewGuid();
            await conn.ExecuteAsync(sql, new
            {
                Id = clientInteractionId,
                UserId = userId,
                ClientId = clientId,
                InteractionType = "Email",
                IsOutgoing = isOutgoing,
                GmailId = gmailMessageId,
                ThreadId = threadId,
                Snippet = snippet,
                Subject = parsed.Subject ?? "",
                //emailBody = emailBody ?? "",
                //emailType = emailType ?? "",
                MessageDate = parsed.Date,
                Raw = JsonSerializer.Serialize(parsed.RawHeaders),
                Parsed = JsonSerializer.Serialize(parsed),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private async Task<(Guid UserId, string UserEmail, string UserRole, string AccessToken, string RefreshToken, DateTimeOffset TokenExpiry)?> GetUserAndTokensAsync(NpgsqlConnection conn, Guid userId)
        {
            var sql = @"SELECT 
            u.UserId, 
            us.email AS user_email,
            us.UserRole,
            u.AccessToken, 
            u.RefreshToken, 
            u.AccessToken_ExpiresAt
            FROM authTokens u
            JOIN users us ON us.id = u.UserId
            WHERE u.UserId = @uid";
            return await conn.QuerySingleOrDefaultAsync<(Guid, string, string, string, string, DateTimeOffset)>(sql, new { uid = userId });
        }

        private async Task EnqueueRecomputeAsync(Guid clientId, string userRole)
        {
            var clientIdString = clientId.ToString();

            var uniqueKey = $"{clientIdString}:{userRole}";

            var req = new SendMessageRequest
            {
                QueueUrl = _scoreQueueUrl,
                MessageBody = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ClientId = clientId,
                    UserRole = userRole
                }),

                // FIFO debounce per client
                MessageGroupId = uniqueKey,
                MessageDeduplicationId = uniqueKey
            };

            await _sqs.SendMessageAsync(req);
        }



        private bool IsAutomatedSystemEmail(IDictionary<string, string> headers)
        {
            // Indicator 1: The "List-Unsubscribe" header is the single best professional indicator of bulk mail.
            if (headers.ContainsKey("List-Unsubscribe")) return true;

            // Indicator 2: Priority/Precedence headers (used by mailing lists/systems)
            if (headers.ContainsKey("Precedence") && headers["Precedence"].Contains("bulk")) return true;

            // Indicator 3: "Auto-Submitted" header (standard for out-of-office replies or lists)
            if (headers.ContainsKey("Auto-Submitted") && headers["Auto-Submitted"] != "no") return true;

            // Indicator 4: Common 'No-Reply' patterns using regex for robust matching
            string from = GetHeaderValue(headers, "From");
            if (!string.IsNullOrEmpty(from) && System.Text.RegularExpressions.Regex.IsMatch(from, @"(no-reply|noreply|auto-reply|postmaster)@", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            return false;
        }

        private string GetHeaderValue(IDictionary<string, string> headers, string headerName)
        {
            return headers.TryGetValue(headerName, out var value) ? value : null;
        }

        // ---------------------------------------------------------------------
        // POLICY: decide if/when to fetch body
        // ---------------------------------------------------------------------
        private IngestionDecision DecideIngestion(string? subject, string snippet, Dictionary<string, string> headers)
        {
            if (_options.AlwaysMetadataOnly)
            {
                return new IngestionDecision
                {
                    Skip = false,
                    BodyDecision = BodyFetchDecision.Never,
                    ReasonKey = "policy.metadata_only"
                };
            }

            bool meaningful = Heuristics.IsLikelyMeaningful(subject, snippet);

            if (!_options.FetchBodyForMeaningfulMessages || !meaningful)
            {
                return new IngestionDecision
                {
                    Skip = false,
                    BodyDecision = BodyFetchDecision.Never,
                    ReasonKey = meaningful ? "policy.meaningful_body_disabled" : "policy.not_meaningful"
                };
            }

            return new IngestionDecision
            {
                Skip = false,
                BodyDecision = _options.StoreEmailBodies ? BodyFetchDecision.FetchAndStore : BodyFetchDecision.FetchEphemeral,
                ReasonKey = _options.StoreEmailBodies ? "policy.fetch_and_store" : "policy.fetch_ephemeral"
            };
        }

        // -------------------------------------------------------------------------
        // Heuristics: meaningfulness without full body (MVP)
        // -------------------------------------------------------------------------
        public static class Heuristics
        {
            private static readonly string[] HighIntentWords = new[]
            {
            "proposal", "quote", "contract", "agreement", "invoice", "payment", "budget", "scope", "timeline",
            "approved", "sign", "signed", "deposit", "terms", "pricing"
        };

            private static readonly Regex MoneyRegex = new(@"\$\s?\d+|\b\d+\s?(usd|inr|eur)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            private static readonly Regex ShortMeaningfulRegex = new(@"\b(approved|ok|okay|yes|no|sounds good|send it|please send|can you|could you|need)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            public static bool IsLikelyMeaningful(string? subject, string snippet)
            {
                var s = $"{subject ?? ""} {snippet ?? ""}".ToLowerInvariant();

                if (HighIntentWords.Any(w => s.Contains(w))) return true;
                if (MoneyRegex.IsMatch(s)) return true;

                if (!string.IsNullOrWhiteSpace(snippet) && snippet.Length <= 60 && ShortMeaningfulRegex.IsMatch(snippet))
                    return true;

                return false;
            }
        }


        // -------------------------------------------------------------------------
        // BODY EXTRACTION: supports payload parts, base64url decoding
        // -------------------------------------------------------------------------
        public static class GmailBodyExtractor
        {
            public sealed class BodyResult
            {
                public string MimeType { get; set; } = "none"; // text/plain or text/html or none
                public string? Body { get; set; }
            }

            public static BodyResult ExtractBestBody(GmailMessage fullMsg)
            {
                if (fullMsg?.Payload == null)
                    return new BodyResult();

                // Prefer text/plain (safer) then text/html
                var plain = FindPartByMime(fullMsg.Payload, "text/plain");
                if (plain != null)
                {
                    return new BodyResult { MimeType = "text/plain", Body = DecodePartBody(plain) };
                }

                var html = FindPartByMime(fullMsg.Payload, "text/html");
                if (html != null)
                {
                    return new BodyResult { MimeType = "text/html", Body = DecodePartBody(html) };
                }

                // Fall back: payload body might be set
                var direct = fullMsg.Payload.Body?.Data;
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    return new BodyResult { MimeType = fullMsg.Payload.MimeType ?? "unknown", Body = DecodeBase64Url(direct) };
                }

                return new BodyResult();
            }

            private static GmailMessagePart? FindPartByMime(GmailMessagePart part, string mimeType)
            {
                if (part == null) return null;
                if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
                    return part;

                if (part.Parts == null) return null;

                foreach (var child in part.Parts)
                {
                    var found = FindPartByMime(child, mimeType);
                    if (found != null) return found;
                }

                return null;
            }

            private static string? DecodePartBody(GmailMessagePart part)
            {
                var data = part?.Body?.Data;
                if (string.IsNullOrWhiteSpace(data)) return null;
                return DecodeBase64Url(data);
            }

            private static string DecodeBase64Url(string base64Url)
            {
                // Gmail uses base64url (RFC 4648 §5) with '-' '_' and without padding.
                var s = base64Url.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                }
                var bytes = Convert.FromBase64String(s);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        // ---------------------------------------------------------------------
        // GMAIL API HELPERS
        // ---------------------------------------------------------------------
        private GmailService CreateGmailService(string accessToken)
        {
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromAccessToken(accessToken);
            return new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "ClientHealthScore"
            });
        }

        private async Task<GmailMessage?> GetMessageMetadataAsync(GmailService svc, string messageId, CancellationToken ct)
        {
            var req = svc.Users.Messages.Get("me", messageId);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            req.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(_options.MetadataHeaders);
            return await req.ExecuteAsync(ct);
        }

        private async Task<GmailMessage?> GetMessageFullAsync(GmailService svc, string messageId, CancellationToken ct)
        {
            var req = svc.Users.Messages.Get("me", messageId);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            return await req.ExecuteAsync(ct);
        }

        private static Dictionary<string, string> ExtractHeaders(GmailMessage msg)
        {
            var headers = msg.Payload?.Headers ?? new List<GmailHeader>();
            return headers
                .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToDictionary(h => h.Name, h => h.Value ?? "", StringComparer.OrdinalIgnoreCase);
        }



        // ---------------------------------------------------------------------
        // DB HELPERS (adapt to your signatures)
        // ---------------------------------------------------------------------
        private async Task<bool> InteractionExistsAsync(NpgsqlConnection conn, Guid userId, string gmailMessageId, CancellationToken ct)
        {
            var sql = @"SELECT 1 FROM clientInteractions WHERE UserId = @UserId AND gmail_message_id = @GmailMessageId LIMIT 1;";
            var found = await conn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(sql, new { UserId = userId, GmailMessageId = gmailMessageId }, cancellationToken: ct));
            return found.HasValue;
        }

        // ---------------------------------------------------------------------
        // Small helpers
        // ---------------------------------------------------------------------
        private static string? Abbreviate(string? s, int maxChars)
        {
            if (s == null) return null;
            if (maxChars <= 0) return null;
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars);
        }

        private class RecomputeMessage
        {
            public Guid ClientId { get; set; }
            public string UserRole { get; set; } = default!;
        }

    }

}
