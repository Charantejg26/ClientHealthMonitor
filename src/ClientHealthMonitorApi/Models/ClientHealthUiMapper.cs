using CommonLib;

namespace ClientHealthMonitorApi.Models
{
    // What you actually return to UI for the client list and detail page.
    public sealed class ClientCardUiModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        // Always OK to show
        public int Score { get; set; }
        public string Tier { get; set; } = "Unknown";

        // Human-readable states (replace numeric sub-scores)
        public string RelationshipState { get; set; } = "Unknown"; // Stable / Drifting / At Risk
        public string ExecutionState { get; set; } = "Unknown";    // On track / Follow-up overdue / You owe a reply / Slow responses
        public string MomentumState { get; set; } = "";            // Active thread / Deal in motion / High intent / ""

        // Small chips/badges for list view
        public List<string> Badges { get; set; } = new();

        // The 3–5 most important bullets to build trust
        public List<string> WhyBullets { get; set; } = new();

        // Primary CTA hint: helps your frontend choose default button + color
        public string PrimaryAction { get; set; } = "Review"; // Reply / Follow up / Nudge / Review

        // Confidence label (don’t show percentage)
        public string ConfidenceLabel { get; set; } = "Normal"; // Normal / Limited data
        public DateTimeOffset? LastContactAt { get; set; }

        public DateTimeOffset? CalculatedAt { get; set; }
        public string Summary { get; set; } = "";
    }

    public sealed class UiMappingOptions
    {
        // Relationship state thresholds
        public int RelationshipStableMin { get; set; } = 75;
        public int RelationshipDriftingMin { get; set; } = 55;

        // Execution state thresholds
        public int ExecutionOnTrackMin { get; set; } = 75;
        public int ExecutionSlowMin { get; set; } = 60;

        // Confidence threshold
        public double LimitedDataCoverageBelow { get; set; } = 0.30;

        // How many bullets to show
        public int MaxWhyBullets { get; set; } = 4;

        // Segment presets
        public static UiMappingOptions ForSegment(Segment s) => s switch
        {
            // Freelancers: longer cycles, slightly less strict on execution
            Segment.Freelancer => new UiMappingOptions
            {
                RelationshipStableMin = 75,
                RelationshipDriftingMin = 55,
                ExecutionOnTrackMin = 72,
                ExecutionSlowMin = 58,
                LimitedDataCoverageBelow = 0.30,
                MaxWhyBullets = 4
            },

            // Real estate: faster cadence expectations, stricter on execution
            Segment.RealEstateAgent => new UiMappingOptions
            {
                RelationshipStableMin = 78,
                RelationshipDriftingMin = 60,
                ExecutionOnTrackMin = 78,
                ExecutionSlowMin = 62,
                LimitedDataCoverageBelow = 0.35,
                MaxWhyBullets = 4
            },

            _ => new UiMappingOptions()
        };
    }

    public static class ClientHealthUiMapper
    {
        /// <summary>
        /// Main mapper: convert scoring result into a UI-ready model.
        /// Copy-paste this into your API layer.
        /// </summary>
        public static ClientCardUiModel ToClientCardUi(ClientHealthScoreResult r, Segment segment)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            var opt = UiMappingOptions.ForSegment(segment);

            var model = new ClientCardUiModel
            {
                Score = r.Score,
                Tier = string.IsNullOrWhiteSpace(r.Tier) ? TierFromScore(r.Score) : r.Tier
            };

            model.ConfidenceLabel = (r.DataCoverage < opt.LimitedDataCoverageBelow) ? "Limited data" : "Normal";

            model.RelationshipState = MapRelationshipState(r.RelationshipHealth, opt);
            model.ExecutionState = MapExecutionState(r, opt);
            model.MomentumState = MapMomentumState(r);

            model.Badges = BuildBadges(r, model);
            model.WhyBullets = BuildWhyBullets(r, opt.MaxWhyBullets);
            model.PrimaryAction = ChoosePrimaryAction(r);
            model.Summary = BuildGridSummary(r);

            return model;
        }

        private static string BuildGridSummary(ClientHealthScoreResult r)
        {
            var parts = new List<string>();

            if(HasReason(r, "execution.awaiting_reply"))
                parts.Add("Client is waiting on your reply");
            else if(HasReason(r, "execution.next_step_overdue"))
                parts.Add("A follow-up may be overdue");
            else if(HasReason(r, "relationship.recency"))
                parts.Add("Thread has gone quiet longer than usual");

            if (HasReason(r, "relationship.cadence_break"))
                parts.Add("Response cadence has slowed");

            if (HasReason(r, "opportunity.metadata_signals"))
                parts.Add("Recent activity suggests the deal is still active");

            if (parts.Count == 0 && r.DataCoverage < 0.30)
                parts.Add("Limited recent activity available");
             
            if (parts.Count == 0)
                parts.Add("Recent email activity looks normal");


            return string.Join(". ", parts.Take(2)) + ".";
        }

        // ------------------- Relationship State -------------------
        private static string MapRelationshipState(int relationshipHealth, UiMappingOptions opt)
        {
            if (relationshipHealth >= opt.RelationshipStableMin) return "Stable";
            if (relationshipHealth >= opt.RelationshipDriftingMin) return "Drifting";
            return "At Risk";
        }

        // ------------------- Execution State -------------------
        // Execution is about YOU: reply owed, overdue follow-up, slow responses.
        private static string MapExecutionState(ClientHealthScoreResult r, UiMappingOptions opt)
        {
            // Highest priority states: explicit signals from reasons
            if (HasReason(r, "execution.awaiting_reply"))
                return "You owe a reply";

            if (HasReason(r, "execution.next_step_overdue"))
                return "Follow-up overdue";

            // Fallback: numeric execution banding (still not shown as a number)
            if (r.ExecutionHealth >= opt.ExecutionOnTrackMin) return "On track";
            if (r.ExecutionHealth >= opt.ExecutionSlowMin) return "Slow responses";
            return "Needs attention";
        }

        // ------------------- Momentum State -------------------
        // Never show OpportunityBoost as a number. Show a human label.
        private static string MapMomentumState(ClientHealthScoreResult r)
        {
            if (HasReason(r, "opportunity.metadata_signals"))
            {
                if (r.OpportunityBoost >= 70) return "Active thread";
                if (r.OpportunityBoost >= 40) return "Deal in motion";
                return "Some momentum";
            }

            if (HasReason(r, "relationship.awaiting_reply_grace")) return "Active thread";
            return "";
        }

        // ------------------- Badges -------------------
        private static List<string> BuildBadges(ClientHealthScoreResult r, ClientCardUiModel model)
        {
            var badges = new List<string>();

            // Relationship
            if (model.RelationshipState == "At Risk") badges.Add("At risk");
            else if (model.RelationshipState == "Drifting") badges.Add("Drifting");

            // Execution
            if (model.ExecutionState == "You owe a reply") badges.Add("Reply due");
            else if (model.ExecutionState == "Follow-up overdue") badges.Add("Overdue");

            // Momentum
            if (!string.IsNullOrWhiteSpace(model.MomentumState))
                badges.Add(model.MomentumState);

            // Confidence
            if (model.ConfidenceLabel == "Limited data")
                badges.Add("Limited data");

            // Keep badge count sane for list view
            return badges.Distinct().Take(4).ToList();
        }

        // ------------------- Why Bullets -------------------
        // Only show the most material deltas (negative first), with clean language.
        private static List<string> BuildWhyBullets(ClientHealthScoreResult r, int max)
        {
            var bullets = new List<string>();

            // Prefer negative impact reasons (users care why they're on the list)
            var ordered = (r.Reasons ?? new List<ScoreReason>())
                .OrderBy(x => x.Delta) // most negative first
                .ThenBy(x => x.Key)
                .ToList();

            foreach (var reason in ordered)
            {
                var b = HumanizeReason(reason);
                if (string.IsNullOrWhiteSpace(b)) continue;

                bullets.Add(b);
                if (bullets.Count >= max) break;
            }

            if (bullets.Count == 0)
                bullets.Add("Score is based on recent email activity and reply patterns.");

            return bullets;
        }

        private static string HumanizeReason(ScoreReason reason)
        {
            // These keys match HealthScoreCalculatorV2_MetadataFirst.cs
            return reason.Key switch
            {
                "relationship.recency" => Trim(reason.Message),
                "relationship.cadence_break" => "Response cadence slowed significantly.",
                "relationship.sentiment" => "Recent messages show concern or friction (from snippet).",
                "relationship.awaiting_reply_grace" => "Client recently replied; you're next.",
                "execution.responsiveness" => "Your replies have been slower than usual.",
                "execution.next_step_overdue" => "A promised follow-up may be overdue.",
                "execution.awaiting_reply" => "Client replied and is waiting on you.",
                "opportunity.metadata_signals" => "Recent inbound activity suggests momentum.",
                _ => string.IsNullOrWhiteSpace(reason.Message) ? "" : Trim(reason.Message)
            };
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            return s.Length <= 120 ? s : s.Substring(0, 117) + "...";
        }

        // ------------------- Primary Action -------------------
        private static string ChoosePrimaryAction(ClientHealthScoreResult r)
        {
            if (HasReason(r, "execution.awaiting_reply")) return "Reply";
            if (HasReason(r, "execution.next_step_overdue")) return "Follow up";
            if (HasReason(r, "relationship.recency") || HasReason(r, "relationship.cadence_break")) return "Nudge";
            return "Review";
        }

        // ------------------- Helpers -------------------
        private static bool HasReason(ClientHealthScoreResult r, string key)
            => (r.Reasons ?? new List<ScoreReason>())
               .Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

        private static string TierFromScore(int score)
        {
            if (score >= 80) return "Hot";
            if (score >= 60) return "Warm";
            if (score >= 40) return "Nurture";
            return "Cold";
        }
    }
}
