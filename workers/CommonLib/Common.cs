namespace CommonLib
{
    public record SyncMessageId(Guid UserId, string GmailMessageId);

    public class GmailMessageMetadata
    {
        public string Id { get; set; }
        public string ThreadId { get; set; }
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public string Snippet { get; set; }
    }

    public enum Segment
    {
        Freelancer,
        RealEstateAgent
    }

    public sealed class ScoreReason
    {
        public string Key { get; set; } = "";
        public double Delta { get; set; }
        public string Message { get; set; } = "";
        public string? EvidenceThreadId { get; set; }
        public DateTime? EvidenceMessageDate { get; set; }
    }
    public class ClientHealthScoreResult
    {
        public int Score { get; set; } // 0..100
        public int RelationshipHealth { get; set; } // 0..100
        public int ExecutionHealth { get; set; } // 0..100
        public int OpportunityBoost { get; set; } // 0..100 (informational)
        public double DataCoverage { get; set; } // 0..1 (coverage/volume)
        public string Tier { get; set; } = "Unknown";
        public List<ScoreReason> Reasons { get; set; } = new();
    }

    public sealed class GmailIngestionOptions
    {
        // If false, we never store email bodies; we can still fetch body ephemerally to generate drafts.
        public bool StoreEmailBodies { get; set; } = false;

        // If true, we will fetch message body for messages that match "meaningful" heuristics.
        // If false, we only ingest metadata + snippet.
        public bool FetchBodyForMeaningfulMessages { get; set; } = true;

        // Hard cap to avoid huge data pulls even if message is large.
        public int MaxBodyCharsToStore { get; set; } = 20_000;

        // Headers we want in metadata mode.
        public string[] MetadataHeaders { get; set; } = new[]
        {
            "From","To","Cc","Bcc","Date","Subject","Message-Id","In-Reply-To","References","Reply-To","List-Unsubscribe"
        };

        // If you want an even more strict mode: never fetch FULL; only Metadata.
        public bool AlwaysMetadataOnly { get; set; } = false;
    }

    public enum BodyFetchDecision
    {
        Never,
        FetchEphemeral, // fetch full to compute signals/draft, but do not persist full body
        FetchAndStore   // fetch full and store (not recommended early unless needed)
    }

    public sealed class IngestionDecision
    {
        public bool Skip { get; set; }
        public string SkipReason { get; set; } = "";
        public BodyFetchDecision BodyDecision { get; set; } = BodyFetchDecision.Never;
        public string ReasonKey { get; set; } = "";
    }

}
