using CommonLib;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static ProcessorService.ProcessorService;

namespace ProcessorService.Utilities
{  

    // ----------------------- Config Models -----------------------

    public sealed class ScoringV2Config
    {
        public Dictionary<string, SegmentProfileConfig> Segments { get; set; } = new();
        public SegmentProfileConfig GetProfile(Segment segment)
        {
            var key = segment.ToString();
            if (Segments.TryGetValue(key, out var profile)) return profile;
            throw new InvalidOperationException($"Missing scoring profile for segment: {key}");
        }
    }


    public sealed class SegmentProfileConfig
    {
        public double RelationshipWeight { get; set; } = 0.60;
        public double ExecutionWeight { get; set; } = 0.25;
        public double OpportunityWeight { get; set; } = 0.15;

        public int BaselineScore { get; set; } = 80;

        public CoverageConfig Coverage { get; set; } = new();
        public RecencyConfig Recency { get; set; } = new();
        public ResponsivenessConfig Responsiveness { get; set; } = new();
        public CadenceBreakConfig CadenceBreak { get; set; } = new();
        public SentimentConfig Sentiment { get; set; } = new();
        public NextStepConfig NextStep { get; set; } = new();

        // Extra metadata-first guardrails
        public MetadataOnlyGuards MetadataGuards { get; set; } = new();

        public List<TierConfig> Tiers { get; set; } = new()
        {
            new TierConfig { Name = "Hot", Min = 80 },
            new TierConfig { Name = "Warm", Min = 60 },
            new TierConfig { Name = "Nurture", Min = 40 },
            new TierConfig { Name = "Cold", Min = 0 }
        };
    }

    public sealed class MetadataOnlyGuards
    {
        // If last inbound is very recent but there is no reply yet, don't penalize recency harshly.
        public int InboundGraceHours { get; set; } = 18;

        // If only snippets are available, treat sentiment as weaker.
        public double SentimentWeightMultiplier { get; set; } = 0.75;

        // If only snippets are available, treat commitment detection as weaker.
        public double CommitmentWeightMultiplier { get; set; } = 0.80;

        // Cap total sentiment penalty impact
        public int MaxEffectiveSentimentPenalty { get; set; } = 15;
    }

    public sealed class TierConfig { public string Name { get; set; } = "Unknown"; public int Min { get; set; } = 0; }

    public sealed class CoverageConfig
    {
        public int MeaningfulExchangesForFullCoverage { get; set; } = 6;
        public int WindowDays { get; set; } = 30;
        public double MinCoverageFloor { get; set; } = 0.15;
    }

    public sealed class RecencyConfig
    {
        public int MaxPenalty { get; set; } = 30;
        public double OverdueMultiple { get; set; } = 2.0;
        public int NoPenaltyIfWithinDays { get; set; } = 0;
    }

    public sealed class ResponsivenessConfig
    {
        public int MaxPenalty { get; set; } = 10;
        public double NoPenaltyGapHours { get; set; } = 6.0;
        public double MaxPenaltyGapHours { get; set; } = 48.0;
        public double MaxConsideredGapHours { get; set; } = 24.0 * 30;
    }

    public sealed class CadenceBreakConfig
    {
        public int Penalty { get; set; } = 10;
        public double BreakMultiplier { get; set; } = 2.5;
        public int MinimumGapDays { get; set; } = 3;
    }

    public sealed class SentimentConfig
    {
        public int NeutralDriftPenalty { get; set; } = 5;
        public int NegativePenalty { get; set; } = 15;
        public int NegativeWithRiskWordsPenalty { get; set; } = 20;

        public List<string> CriticalRiskWords { get; set; } = new() { "cancel", "refund", "lawyer", "legal", "terminate", "unacceptable" };
        public List<string> FrustrationWords { get; set; } = new() { "disappointed", "waiting", "update", "delayed", "not happy", "issue", "problem" };
        public List<string> PositiveWords { get; set; } = new() { "thanks", "great", "perfect", "awesome", "love", "helpful" };
    }

    public sealed class NextStepConfig
    {
        public int Overdue1To3DaysPenalty { get; set; } = 10;
        public int Overdue4To7DaysPenalty { get; set; } = 15;
        public int Overdue8PlusDaysPenalty { get; set; } = 20;

        public int LookbackDays { get; set; } = 14;

        public List<string> CommitmentPhrases { get; set; } = new()
        {
            "i will send", "i'll send", "i will share", "i'll share",
            "i will follow up", "i'll follow up", "will provide", "get back to you"
        };
    }

    //public enum Sentiment
    //{
    //    Positive,
    //    Neutral,
    //    NeutralDrift,
    //    Negative,
    //    NegativeWithRiskWords
    //}


    // ----------------------- Interaction Model -----------------------

    public sealed class ClientInteractionDto
    {
        public DateTime MessageDate { get; set; }
        public bool IsOutgoing { get; set; }
        public string? GmailThreadId { get; set; }
        public string? Subject { get; set; }
        public string? Snippet { get; set; }
        // FullBody intentionally ignored in metadata-first scoring.
    }

    public sealed class ClientBaselines
    {
        public double TypicalCadenceDays { get; set; } = 7.0;
        public double DaysSinceLastMeaningful { get; set; } = 999.0;
        public double YourAvgReplyHours { get; set; } = 0.0;
        public double TheirAvgReplyHours { get; set; } = 0.0;
        public bool CadenceBroken { get; set; } = false;

        public double DataCoverage { get; set; } = 0.0;
        public int MeaningfulExchangesInWindow { get; set; } = 0;

        public DateTime? LastInboundAt { get; set; }
        public DateTime? LastOutboundAt { get; set; }
        public bool AwaitingYourReply { get; set; } = false;
    }

    public enum SentimentBucket
    {
        PositiveOrNeutral,
        NeutralDrift,
        Negative,
        NegativeWithRiskWords
    }

    public sealed class OverdueNextStep
    {
        public string Details { get; set; } = "";
        public int DaysOverdue { get; set; }
        public string? EvidenceThreadId { get; set; }
        public DateTime? EvidenceMessageDate { get; set; }
    }

    //public enum EngagementStage
    //{
    //    Lead, // A potential customer who has shown interest but hasn't committed.
    //    Onboarding, //The client is actively setting up the service. This stage usually demands high engagement and frequent meetings.
    //    ActiveDeal, //A proposal is out, and negotiations are active.
    //    SteadyState, //The client is a standard, ongoing customer with regular, lower-frequency engagement.
    //    RenewalWindow, // The contract is coming up for renewal, requiring proactive attention and meetings.
    //    Churned // The client has ended their relationship.(Score should be 0, typically).
    //}

    //public class ClientInteractionDto
    //{
    //    public DateTime MessageDate { get; set; }
    //    public bool IsOutgoing { get; set; }
    //    public string? FullBody { get; set; }
    //    public string? Snippet { get; set; }
    //    public string? Subject { get; set; }
    //}

    //public class ClientBaselines
    //{
    //    // Median meaningful exchange gap in days, last 90d (fallback 7 days)
    //    public double TypicalCadenceDays { get; set; } = 7.0; // This explains, How often does this client usually talk to us?
    //    // Min(1, meaningful_exchanges_last_30d / 6)
    //    public double Confidence { get; set; }  // Do we have enough data to trust this score?
    //    public EngagementStage Stage { get; set; } // Where are they in the sales/renewal cycle?
    //    public double DaysSinceLastMeaningfulExchange { get; set; } //How long has it been since they sent a useful email?
    //    //public double DaysSinceLastMeeting { get; set; }
    //    // Avg reply times in hours
    //    public double YourAvgReplyHours { get; set; } //How long do we take to reply?
    //    public double TheirAvgReplyHours { get; set; } //How long do they take to reply?
    //    public bool HasCadenceBrokenFlag { get; set; } // Has their recent behavior changed abruptly?
    //}

    //public class OverdueNextStep
    //{
    //    public string CommitmentDetails { get; set; }
    //    public int DaysOverdue { get; set; }
    //}

    // --- Main Service Logic ---

    public class HealthScoreCalculatorService
    {
        private readonly string _connString;
        private readonly ScoringV2Config _config;
        public HealthScoreCalculatorService(IConfiguration config)
        {
            _connString = config.GetConnectionString("PostgreConnection") ?? "";
            _config = config.GetSection("ScoringV2").Get<ScoringV2Config>()
                      ?? throw new InvalidOperationException("Missing ScoringV2 config section.");
        }

        public async Task CalculateAndPersistScoreAsync(Guid clientId, Segment segment, CancellationToken ct)
        {
            using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync();

            // 1. Automatically Infer the Stage
            //var engagementStageString = await InferEngagementStageAsync(conn, clientId, ct);

            // Convert the string stage back to C# Enum internally for robust penalty logic
            //if (!Enum.TryParse(engagementStageString, out EngagementStage currentStage))
            //{
            //    currentStage = EngagementStage.Lead; // Fallback if inference returns junk
            //}

            // 2. Run the main scoring logic
            var scoreResult = await CalculateAsync(clientId, segment, ct);

            // 3. Persist the results to both tables
            //await UpdateClientStageInClientsTable(conn, clientId, engagementStageString, ct); // Update the Clients table
            await UpsertClientHealthScore(conn, clientId, scoreResult, ct); // Insert a new row into ClientScore table
        }

        public async Task<ClientHealthScoreResult> CalculateAsync(Guid clientId, Segment segment, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);

            var profile = _config.GetProfile(segment);

            var interactions = (await GetRecentInteractionsAsync(conn, clientId, ct)).ToList();
            var baselines = await ComputeBaselinesAsync(conn, clientId, interactions, profile, ct);

            var relationshipPart = ScoreRelationship(profile, baselines, interactions);
            var executionPart = await ScoreExecutionAsync(conn, clientId, profile, baselines, interactions, ct);
            var opportunityPart = ScoreOpportunity(profile, baselines, interactions);

            return ComposeFinal(profile, baselines, relationshipPart, executionPart, opportunityPart);
        }
        // ---------------- Data Access ----------------

        private async Task<IEnumerable<ClientInteractionDto>> GetRecentInteractionsAsync(NpgsqlConnection conn, Guid clientId, CancellationToken ct)
        {
            // IMPORTANT: Only subject/snippet and metadata are required for scoring.
            var sql = @"
                SELECT
                    message_date AS MessageDate,
                    isoutgoing AS IsOutgoing,
                    gmail_thread_id AS GmailThreadId,
                    subject AS Subject,
                    snippet AS Snippet
                FROM clientInteractions
                WHERE ClientId = @ClientId
                ORDER BY message_date DESC
                LIMIT 300;";

            return await conn.QueryAsync<ClientInteractionDto>(new CommandDefinition(sql, new { ClientId = clientId }, cancellationToken: ct));
        }

        // ---------------- Baselines ----------------

        private async Task<ClientBaselines> ComputeBaselinesAsync(
            NpgsqlConnection conn,
            Guid clientId,
            List<ClientInteractionDto> interactions,
            SegmentProfileConfig profile,
            CancellationToken ct)
        {
            var meaningful = interactions
                .Where(IsMeaningful_MetadataOnly)
                .OrderByDescending(i => i.MessageDate)
                .ToList();

            var meaningfulDates = meaningful.Select(m => m.MessageDate).ToList();
            var gaps = new List<double>();
            for (int i = 0; i < meaningfulDates.Count - 1; i++)
            {
                gaps.Add((meaningfulDates[i] - meaningfulDates[i + 1]).TotalDays);
            }

            var typicalCadence = gaps.Count > 0 ? Median(gaps) : 7.0;
            typicalCadence = Math.Clamp(typicalCadence, 1.0, 30.0);

            var daysSinceLast = meaningfulDates.Count > 0
                ? (DateTime.UtcNow - meaningfulDates[0].ToUniversalTime()).TotalDays
                : 999.0;

            // Coverage
            var cutoff = DateTime.UtcNow.AddDays(-profile.Coverage.WindowDays);
            var exchangesInWindow = meaningful.Count(m => m.MessageDate.ToUniversalTime() >= cutoff);
            var coverage = exchangesInWindow / (double)profile.Coverage.MeaningfulExchangesForFullCoverage;
            coverage = Math.Clamp(coverage, 0.0, 1.0);
            coverage = Math.Max(coverage, profile.Coverage.MinCoverageFloor);

            // Reply times (thread LEAD)
            var yourAvgReply = await CalculateYourAverageReplyTimeHoursAsync(conn, clientId, profile.Responsiveness.MaxConsideredGapHours, ct);
            var theirAvgReply = await CalculateTheirAverageReplyTimeHoursAsync(conn, clientId, profile.Responsiveness.MaxConsideredGapHours, ct);

            // Last inbound/outbound + awaiting reply
            var lastInbound = interactions.Where(i => !i.IsOutgoing).OrderByDescending(i => i.MessageDate).FirstOrDefault();
            var lastOutbound = interactions.Where(i => i.IsOutgoing).OrderByDescending(i => i.MessageDate).FirstOrDefault();
            bool awaitingYourReply = lastInbound != null && (lastOutbound == null || lastOutbound.MessageDate < lastInbound.MessageDate);

            var cadenceBroken = ComputeCadenceBrokenFlag(meaningfulDates, typicalCadence, profile.CadenceBreak);

            return new ClientBaselines
            {
                TypicalCadenceDays = typicalCadence,
                DaysSinceLastMeaningful = daysSinceLast,
                YourAvgReplyHours = yourAvgReply,
                TheirAvgReplyHours = theirAvgReply,
                CadenceBroken = cadenceBroken,
                DataCoverage = coverage,
                MeaningfulExchangesInWindow = exchangesInWindow,
                LastInboundAt = lastInbound?.MessageDate,
                LastOutboundAt = lastOutbound?.MessageDate,
                AwaitingYourReply = awaitingYourReply
            };
        }

        // ---------------- Relationship scoring ----------------

        private ClientHealthScoreResult ScoreRelationship(
            SegmentProfileConfig profile,
            ClientBaselines b,
            List<ClientInteractionDto> interactions)
        {
            var reasons = new List<ScoreReason>();
            double relationship = profile.BaselineScore;

            // Guard: if client just emailed recently and we're awaiting reply, don't punish recency yet.
            if (b.AwaitingYourReply && b.LastInboundAt != null)
            {
                var hours = (DateTime.UtcNow - b.LastInboundAt.Value.ToUniversalTime()).TotalHours;
                if (hours <= profile.MetadataGuards.InboundGraceHours)
                {
                    reasons.Add(new ScoreReason
                    {
                        Key = "relationship.awaiting_reply_grace",
                        Delta = 0,
                        Message = $"Awaiting your reply; inbound was {hours:F0}h ago (grace {profile.MetadataGuards.InboundGraceHours}h) → no recency penalty"
                    });

                    // Still compute sentiment/cadence but recency penalty is suppressed.
                    goto SentimentAndCadence;
                }
            }

            // 1) Recency vs cadence
            var recPenalty = ComputeRecencyPenalty(profile.Recency, b);
            relationship += recPenalty;
            if (recPenalty < 0)
            {
                reasons.Add(new ScoreReason
                {
                    Key = "relationship.recency",
                    Delta = recPenalty,
                    Message = $"Silent {b.DaysSinceLastMeaningful:F0}d vs cadence {b.TypicalCadenceDays:F0}d → {recPenalty}"
                });
            }

            SentimentAndCadence:

            // 2) Cadence break (downweight with coverage)
            if (b.CadenceBroken)
            {
                var pen = -Math.Abs(profile.CadenceBreak.Penalty) * b.DataCoverage;
                relationship += pen;

                reasons.Add(new ScoreReason
                {
                    Key = "relationship.cadence_break",
                    Delta = pen,
                    Message = $"Cadence break detected → {pen:F0} (coverage {b.DataCoverage:F2})"
                });
            }

            // 3) Sentiment from Subject+Snippet only (conservative)
            var sent = ComputeSentimentBucket_MetadataOnly(profile.Sentiment, interactions);
            var rawPenalty = sent switch
            {
                SentimentBucket.NegativeWithRiskWords => -profile.Sentiment.NegativeWithRiskWordsPenalty,
                SentimentBucket.Negative => -profile.Sentiment.NegativePenalty,
                SentimentBucket.NeutralDrift => -profile.Sentiment.NeutralDriftPenalty,
                _ => 0
            };

            // Metadata-only: reduce impact, then coverage-weight, then cap effective penalty.
            var weighted = rawPenalty * profile.MetadataGuards.SentimentWeightMultiplier * b.DataCoverage;
            weighted = Math.Max(weighted, -profile.MetadataGuards.MaxEffectiveSentimentPenalty);

            relationship += weighted;

            if (weighted < 0)
            {
                var lastInbound = interactions.Where(i => !i.IsOutgoing).OrderByDescending(i => i.MessageDate).FirstOrDefault();
                reasons.Add(new ScoreReason
                {
                    Key = "relationship.sentiment",
                    Delta = weighted,
                    Message = $"Inbound tone from snippet: {sent} → {weighted:F0} (coverage {b.DataCoverage:F2})",
                    EvidenceThreadId = lastInbound?.GmailThreadId,
                    EvidenceMessageDate = lastInbound?.MessageDate
                });
            }

            var relationshipClamped = (int)Math.Round(Math.Clamp(relationship, 0.0, 100.0));
            return new ClientHealthScoreResult
            {
                RelationshipHealth = relationshipClamped,
                DataCoverage = b.DataCoverage,
                Reasons = reasons
            };
        }

        // ---------------- Execution scoring ----------------

        private async Task<ClientHealthScoreResult> ScoreExecutionAsync(
            NpgsqlConnection conn,
            Guid clientId,
            SegmentProfileConfig profile,
            ClientBaselines b,
            List<ClientInteractionDto> interactions,
            CancellationToken ct)
        {
            var reasons = new List<ScoreReason>();
            double execution = profile.BaselineScore;

            // 1) Responsiveness penalty (your speed vs theirs). If we're awaiting your reply, this matters more than "relationship".
            var respPenalty = ComputeResponsivenessPenalty(profile.Responsiveness, b);
            respPenalty *= b.DataCoverage; // volume-weight
            execution += respPenalty;

            if (respPenalty < 0)
            {
                reasons.Add(new ScoreReason
                {
                    Key = "execution.responsiveness",
                    Delta = respPenalty,
                    Message = $"Your avg reply {b.YourAvgReplyHours:F0}h vs theirs {b.TheirAvgReplyHours:F0}h → {respPenalty:F0} (coverage {b.DataCoverage:F2})"
                });
            }

            // 2) Next-step overdue (commitments) from outgoing Subject+Snippet only
            var overdueSteps = await DetectOverdueNextSteps_MetadataOnlyAsync(conn, clientId, profile.NextStep, ct);
            foreach (var step in overdueSteps)
            {
                var basePen = -ComputeOverduePenalty(profile.NextStep, step.DaysOverdue);

                // Metadata-only: reduce impact slightly, and don't over-penalize with low coverage
                var weighted = basePen * profile.MetadataGuards.CommitmentWeightMultiplier * Math.Clamp(0.5 + 0.5 * b.DataCoverage, 0.5, 1.0);
                execution += weighted;

                reasons.Add(new ScoreReason
                {
                    Key = "execution.next_step_overdue",
                    Delta = weighted,
                    Message = $"Overdue commitment (snippet-based): {step.Details} ({step.DaysOverdue}d) → {weighted:F0}",
                    EvidenceThreadId = step.EvidenceThreadId,
                    EvidenceMessageDate = step.EvidenceMessageDate
                });
            }

            // If we are clearly awaiting your reply (client emailed last), add a small execution nudge (not a penalty explosion)
            if (b.AwaitingYourReply && b.LastInboundAt != null)
            {
                var hours = (DateTime.UtcNow - b.LastInboundAt.Value.ToUniversalTime()).TotalHours;
                if (hours >= 24)
                {
                    var nudge = -Math.Min(6, Math.Floor((hours - 24) / 24) * 2); // -2 per day after 24h, cap -6
                    nudge *= Math.Clamp(0.5 + 0.5 * b.DataCoverage, 0.5, 1.0);
                    execution += nudge;

                    reasons.Add(new ScoreReason
                    {
                        Key = "execution.awaiting_reply",
                        Delta = nudge,
                        Message = $"Client wrote {hours:F0}h ago and you're last to respond → {nudge:F0}"
                    });
                }
            }

            var executionClamped = (int)Math.Round(Math.Clamp(execution, 0.0, 100.0));
            return new ClientHealthScoreResult
            {
                ExecutionHealth = executionClamped,
                DataCoverage = b.DataCoverage,
                Reasons = reasons
            };
        }

        // ---------------- Opportunity scoring ----------------

        private ClientHealthScoreResult ScoreOpportunity(
            SegmentProfileConfig profile,
            ClientBaselines b,
            List<ClientInteractionDto> interactions)
        {
            // Conservative: infer opportunity from recent inbound movement + high-intent snippet/subject terms.
            double opp = 0.0;
            var reasons = new List<ScoreReason>();

            var lastInbound = interactions.Where(i => !i.IsOutgoing).OrderByDescending(i => i.MessageDate).FirstOrDefault();
            if (lastInbound != null)
            {
                var days = (DateTime.UtcNow - lastInbound.MessageDate.ToUniversalTime()).TotalDays;

                // Recent inbound implies deal is in motion.
                if (days <= 1) opp += 50;
                else if (days <= 3) opp += 35;
                else if (days <= 7) opp += 20;

                // High-intent words in subject/snippet bumps opportunity a bit.
                if (Heuristics.IsLikelyMeaningful(lastInbound.Subject, lastInbound.Snippet ?? ""))
                    opp += 20;

                opp = Math.Clamp(opp, 0, 100);

                if (opp > 0)
                {
                    reasons.Add(new ScoreReason
                    {
                        Key = "opportunity.metadata_signals",
                        Delta = opp,
                        Message = $"Recent inbound + intent terms → opportunity {opp:F0}",
                        EvidenceThreadId = lastInbound.GmailThreadId,
                        EvidenceMessageDate = lastInbound.MessageDate
                    });
                }
            }

            return new ClientHealthScoreResult
            {
                OpportunityBoost = (int)Math.Round(opp),
                DataCoverage = b.DataCoverage,
                Reasons = reasons
            };
        }

        // ---------------- Final composition ----------------

        private ClientHealthScoreResult ComposeFinal(
            SegmentProfileConfig profile,
            ClientBaselines b,
            ClientHealthScoreResult relationshipPart,
            ClientHealthScoreResult executionPart,
            ClientHealthScoreResult opportunityPart)
        {
            var rel = relationshipPart.RelationshipHealth;
            var exe = executionPart.ExecutionHealth;
            var opp = opportunityPart.OpportunityBoost;

            // Opportunity as mild positive bias: 0..100 -> 0..10 lift
            var oppLift = (opp / 100.0) * 10.0;

            var raw = (profile.RelationshipWeight * rel) +
                      (profile.ExecutionWeight * exe) +
                      (profile.OpportunityWeight * 100.0 * (oppLift / 10.0));

            // Shrink-to-mean with coverage
            var baseline = profile.BaselineScore;
            var shrink = 0.5 + 0.5 * b.DataCoverage;
            var finalRaw = baseline + (raw - baseline) * shrink;

            var finalScore = (int)Math.Round(Math.Clamp(finalRaw, 0.0, 100.0));
            var tier = ResolveTier(profile.Tiers, finalScore);

            var allReasons = new List<ScoreReason>();
            allReasons.AddRange(relationshipPart.Reasons);
            allReasons.AddRange(executionPart.Reasons);
            allReasons.AddRange(opportunityPart.Reasons);

            return new ClientHealthScoreResult
            {
                Score = finalScore,
                RelationshipHealth = rel,
                ExecutionHealth = exe,
                OpportunityBoost = opp,
                DataCoverage = b.DataCoverage,
                Tier = tier,
                Reasons = allReasons
            };
        }

        private static string ResolveTier(List<TierConfig> tiers, int score)
        {
            var ordered = tiers.OrderByDescending(t => t.Min).ToList();
            foreach (var t in ordered)
            {
                if (score >= t.Min) return t.Name;
            }
            return ordered.LastOrDefault()?.Name ?? "Unknown";
        }


        // ---------------- Penalty computations ----------------

        private static double ComputeRecencyPenalty(RecencyConfig cfg, ClientBaselines b)
        {
            if (b.DaysSinceLastMeaningful <= cfg.NoPenaltyIfWithinDays) return 0.0;

            var overdue = Math.Max(0.0, b.DaysSinceLastMeaningful - b.TypicalCadenceDays);
            var denom = Math.Max(1e-6, cfg.OverdueMultiple * b.TypicalCadenceDays);
            var t = Math.Clamp(overdue / denom, 0.0, 1.0);
            var pen = -cfg.MaxPenalty * t;
            return Math.Round(pen, 0);
        }

        private static double ComputeResponsivenessPenalty(ResponsivenessConfig cfg, ClientBaselines b)
        {
            if (b.YourAvgReplyHours <= 0 || b.TheirAvgReplyHours <= 0) return 0.0;

            var gap = b.YourAvgReplyHours - b.TheirAvgReplyHours;

            if (gap <= cfg.NoPenaltyGapHours) return 0.0;
            if (gap >= cfg.MaxPenaltyGapHours) return -cfg.MaxPenalty;

            var t = (gap - cfg.NoPenaltyGapHours) / (cfg.MaxPenaltyGapHours - cfg.NoPenaltyGapHours);
            t = Math.Clamp(t, 0.0, 1.0);
            var pen = -cfg.MaxPenalty * t;
            return Math.Round(pen, 0);
        }

        private static int ComputeOverduePenalty(NextStepConfig cfg, int daysOverdue)
        {
            if (daysOverdue >= 8) return cfg.Overdue8PlusDaysPenalty;
            if (daysOverdue >= 4) return cfg.Overdue4To7DaysPenalty;
            if (daysOverdue >= 1) return cfg.Overdue1To3DaysPenalty;
            return 0;
        }

        private static bool ComputeCadenceBrokenFlag(List<DateTime> meaningfulDatesDesc, double typicalCadenceDays, CadenceBreakConfig cfg)
        {
            if (meaningfulDatesDesc == null || meaningfulDatesDesc.Count < 2) return false;

            var recentGap = (meaningfulDatesDesc[0] - meaningfulDatesDesc[1]).TotalDays;
            if (recentGap < cfg.MinimumGapDays) return false;
            if (recentGap > typicalCadenceDays * cfg.BreakMultiplier) return true;
            return false;
        }

        // ---------------- Sentiment (metadata only) ----------------

        private static SentimentBucket ComputeSentimentBucket_MetadataOnly(SentimentConfig cfg, List<ClientInteractionDto> interactions)
        {
            // Only inbound, last 5
            var lastInbound = interactions
                .Where(i => !i.IsOutgoing)
                .OrderByDescending(i => i.MessageDate)
                .Take(5)
                .ToList();

            if (lastInbound.Count == 0) return SentimentBucket.PositiveOrNeutral;

            double riskScore = 0.0;
            int negMsgs = 0;

            for (int i = 0; i < lastInbound.Count; i++)
            {
                var msg = lastInbound[i];
                var text = NormalizeText_MetadataOnly($"{msg.Subject ?? ""} {msg.Snippet ?? ""}");

                double msgRisk = 0.0;

                if (cfg.CriticalRiskWords.Any(w => text.Contains(w))) msgRisk += 5.0;
                if (cfg.FrustrationWords.Any(w => text.Contains(w))) msgRisk += 2.0;
                if (cfg.PositiveWords.Any(w => text.Contains(w))) msgRisk -= 1.0;

                var weight = Math.Max(0.2, 1.0 - (i * 0.2));
                riskScore += msgRisk * weight;
                if (msgRisk > 0) negMsgs++;
            }

            if (riskScore >= 4.0) return SentimentBucket.NegativeWithRiskWords;
            if (negMsgs >= 2 || riskScore >= 2.0) return SentimentBucket.Negative;
            if (riskScore >= 0.5) return SentimentBucket.NeutralDrift;

            return SentimentBucket.PositiveOrNeutral;
        }

        private static string NormalizeText_MetadataOnly(string input)
        {
            var s = (input ?? "").ToLowerInvariant();
            s = Regex.Replace(s, @"(\r\n|\r|\n)+", " ");
            s = Regex.Replace(s, @"\s+", " ");
            return s;
        }

        // ---------------- Next-step detection (metadata only) ----------------

        private async Task<List<OverdueNextStep>> DetectOverdueNextSteps_MetadataOnlyAsync(NpgsqlConnection conn, Guid clientId, NextStepConfig cfg, CancellationToken ct)
        {
            var sql = @"
                SELECT
                    gmail_thread_id AS GmailThreadId,
                    message_date AS MessageDate,
                    subject AS Subject,
                    snippet AS Snippet,
                    isoutgoing AS IsOutgoing
                FROM clientInteractions
                WHERE ClientId = @ClientId
                  AND IsOutgoing = TRUE
                  AND message_date > (NOW() - (@LookbackDays || ' days')::interval)
                ORDER BY message_date DESC
                LIMIT 120;";

            var rows = (await conn.QueryAsync<ClientInteractionDto>(new CommandDefinition(
                sql,
                new { ClientId = clientId, LookbackDays = cfg.LookbackDays },
                cancellationToken: ct
            ))).ToList();

            var overdue = new List<OverdueNextStep>();
            var now = DateTime.UtcNow;

            foreach (var msg in rows)
            {
                var text = NormalizeText_MetadataOnly($"{msg.Subject ?? ""} {msg.Snippet ?? ""}");
                if (!cfg.CommitmentPhrases.Any(p => text.Contains(p))) continue;

                var days = (int)Math.Floor((now - msg.MessageDate.ToUniversalTime()).TotalDays);
                if (days <= 3) continue; // reduce noise

                overdue.Add(new OverdueNextStep
                {
                    Details = $"Commitment phrase detected in outgoing snippet on {msg.MessageDate:MMM dd}",
                    DaysOverdue = days,
                    EvidenceThreadId = msg.GmailThreadId,
                    EvidenceMessageDate = msg.MessageDate
                });

                break; // MVP: keep most recent commitment only
            }

            return overdue;
        }


        // ---------------- Reply time SQL ----------------

        private async Task<double> CalculateYourAverageReplyTimeHoursAsync(NpgsqlConnection conn, Guid clientId, double maxGapHours, CancellationToken ct)
        {
            var sql = @"
                WITH Threaded AS (
                    SELECT
                        message_date,
                        isoutgoing,
                        gmail_thread_id,
                        LEAD(message_date) OVER (PARTITION BY gmail_thread_id ORDER BY message_date) AS next_message_date,
                        LEAD(isoutgoing) OVER (PARTITION BY gmail_thread_id ORDER BY message_date) AS next_isoutgoing
                    FROM clientInteractions
                    WHERE ClientId = @ClientId
                      AND gmail_thread_id IS NOT NULL
                ),
                Gaps AS (
                    SELECT EXTRACT(EPOCH FROM (next_message_date - message_date))/3600 AS gap_hours
                    FROM Threaded
                    WHERE isoutgoing = FALSE
                      AND next_message_date IS NOT NULL
                      AND next_isoutgoing = TRUE
                      AND EXTRACT(EPOCH FROM (next_message_date - message_date))/3600 < @MaxGapHours
                )
                SELECT COALESCE(AVG(gap_hours), 0) FROM Gaps;";

            return await conn.QuerySingleAsync<double>(new CommandDefinition(sql, new { ClientId = clientId, MaxGapHours = maxGapHours }, cancellationToken: ct));
        }

        private async Task<double> CalculateTheirAverageReplyTimeHoursAsync(NpgsqlConnection conn, Guid clientId, double maxGapHours, CancellationToken ct)
        {
            var sql = @"
                WITH Threaded AS (
                    SELECT
                        message_date,
                        isoutgoing,
                        gmail_thread_id,
                        LEAD(message_date) OVER (PARTITION BY gmail_thread_id ORDER BY message_date) AS next_message_date,
                        LEAD(isoutgoing) OVER (PARTITION BY gmail_thread_id ORDER BY message_date) AS next_isoutgoing
                    FROM clientInteractions
                    WHERE ClientId = @ClientId
                      AND gmail_thread_id IS NOT NULL
                ),
                Gaps AS (
                    SELECT EXTRACT(EPOCH FROM (next_message_date - message_date))/3600 AS gap_hours
                    FROM Threaded
                    WHERE isoutgoing = TRUE
                      AND next_message_date IS NOT NULL
                      AND next_isoutgoing = FALSE
                      AND EXTRACT(EPOCH FROM (next_message_date - message_date))/3600 < @MaxGapHours
                )
                SELECT COALESCE(AVG(gap_hours), 0) FROM Gaps;";

            return await conn.QuerySingleAsync<double>(new CommandDefinition(sql, new { ClientId = clientId, MaxGapHours = maxGapHours }, cancellationToken: ct));
        }


        // ---------------- Meaningfulness (metadata only) ----------------

        private static bool IsMeaningful_MetadataOnly(ClientInteractionDto i)
        {
            // Conservative: treat as meaningful if (subject+snippet) indicates intent
            // OR if snippet length suggests real content.
            var subject = i.Subject ?? "";
            var snippet = i.Snippet ?? "";

            if (Heuristics.IsLikelyMeaningful(subject, snippet)) return true;
            if (snippet.Length >= 120) return true; // longer snippets usually mean substance

            // Some very short replies can still be meaningful
            if (snippet.Length <= 60 && Heuristics.ShortMeaningfulRegex.IsMatch(snippet)) return true;

            return false;
        }

        // ---------------- Stats helpers ----------------

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var sorted = values.OrderBy(x => x).ToList();
            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 0) return (sorted[mid - 1] + sorted[mid]) / 2.0;
            return sorted[mid];
        }        



        //private async Task UpdateClientStageInClientsTable(NpgsqlConnection conn, Guid clientId, string stage, CancellationToken ct)
        //{
        //    // Update the Clients table with the latest inferred stage string
        //    var sql = @"
        //    UPDATE clients
        //    SET EngagementStage = @Stage, UpdatedAt = now()
        //    WHERE Id = @ClientId";

        //    await conn.ExecuteAsync(sql, new { Stage = stage, ClientId = clientId });
        //}

        private async Task UpsertClientHealthScore(NpgsqlConnection conn, Guid clientId, ClientHealthScoreResult clientHealthScoreResult, CancellationToken ct)
        {

            // 3. Combined SQL script (Update + Insert)
            var sql = @"
                  INSERT INTO clientscores (
                     Id, ClientId, Score, Summary, CalculatedAt, CreatedAt, UpdatedAt, RelationshipHealth, ExecutionHealth, OpportunityBoost, DataCoverage, Tier
                     ) VALUES (
                         @Id, @ClientId, @Score, @Summary, @CalculatedAt, @CreatedAt, @UpdatedAt, @RelationshipHealth, @ExecutionHealth, @OpportunityBoost, @DataCoverage, @Tier
                     )
                     ON CONFLICT (ClientId) DO UPDATE SET 
                     Score = EXCLUDED.Score,
                     Summary = EXCLUDED.Summary,
                     RelationshipHealth = EXCLUDED.RelationshipHealth,
                     ExecutionHealth = EXCLUDED.ExecutionHealth,
                     OpportunityBoost = EXCLUDED.OpportunityBoost,
                     DataCoverage = EXCLUDED.DataCoverage,
                     Tier = EXCLUDED.Tier,
                     CalculatedAt = EXCLUDED.CalculatedAt,
                     UpdatedAt = EXCLUDED.UpdatedAt;";

            // 4. Execute both in one call
            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                Score = clientHealthScoreResult.Score,
                RelationshipHealth = clientHealthScoreResult.RelationshipHealth,
                ExecutionHealth = clientHealthScoreResult.ExecutionHealth,
                OpportunityBoost = clientHealthScoreResult.OpportunityBoost,
                DataCoverage = clientHealthScoreResult.DataCoverage,
                Tier = clientHealthScoreResult.Tier,
                //RiskLevel = clientHealthScoreResult.ConfidenceDescription,
                //Confidence = clientHealthScoreResult.Confidence,
                Summary = string.Join("; ", clientHealthScoreResult.Reasons.Select(r => r.Message)),
                CalculatedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow

            });

        }

    }

    public static class Extensions
    {
        public static double Median(this IEnumerable<double> source)
        {
            var list = source.OrderBy(x => x).ToList();
            if (!list.Any()) return 0.0;
            int mid = list.Count / 2;
            if (list.Count % 2 == 0)
            {
                // If even number of items, average the two middle ones
                return (list[mid - 1] + list[mid]) / 2;
            }
            else
            {
                // If odd number of items, take the exact middle one
                return list[mid];
            }
        }
    }


    // ------------------ Shared heuristics (metadata only) ------------------

    public static class Heuristics
    {
        private static readonly string[] HighIntentWords = new[]
        {
            "proposal", "quote", "contract", "agreement", "invoice", "payment", "budget", "scope", "timeline",
            "approved", "sign", "signed", "deposit", "terms", "pricing", "revision", "estimate"
        };

        private static readonly Regex MoneyRegex = new(@"\$\s?\d+|\b\d+\s?(usd|inr|eur)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly Regex ShortMeaningfulRegex = new(@"\b(approved|ok|okay|yes|no|sounds good|send it|please send|can you|could you|need|let's do it)\b",
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
}
