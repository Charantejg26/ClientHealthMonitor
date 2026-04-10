using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CommonLib
{
    public static class EmailHeaderParser
    {
        public static dynamic Parse(IDictionary<string, string> headers, long? internalDate)
        {
            // Use System.Net.Mail.MailAddress to parse addresses; for complex cases consider MimeKit
            var result = new ExpandoObject() as IDictionary<string, object?>;
            result["RawHeaders"] = headers;
            result["From"] = headers.TryGetValue("From", out var f) ? ParseAddressList(f).FirstOrDefault() : null;
            result["To"] = headers.TryGetValue("To", out var t) ? ParseAddressList(t) : new List<string>();
            result["Cc"] = headers.TryGetValue("Cc", out var c) ? ParseAddressList(c) : new List<string>();
            result["Subject"] = headers.TryGetValue("Subject", out var s) ? s : null;
            //result["Date"] = headers.TryGetValue("Date", out var d) && DateTimeOffset.TryParse(d.Replace(" (UTC)", ""),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal, out var dt) ? dt.UtcDateTime : (DateTime?)null;
            result["Date"] = internalDate.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(internalDate.Value).UtcDateTime
                : DateTimeOffset.UnixEpoch; // you could use the Epoch (1970) as a "flag" for bad data

            return result;
        }

        public static List<(string Address, string Name)> ParseAddressList(string headerValue)
        {
            try
            {
                // MimeKit is recommended for robust parsing; fallback to simple split
                var list = new List<(string Address, string Name)>();
                var parts = headerValue.Split(',');
                foreach (var p in parts)
                {
                    try
                    {
                        var m = new System.Net.Mail.MailAddress(p.Trim());
                        list.Add((m.Address.ToLowerInvariant(), m.DisplayName));
                    }
                    catch
                    {
                        // ignore
                    }
                }
                return list;
            }
            catch
            {
                return new List<(string, string)>();
            }
        }
    }

    public static class EmailUtils
    {
        public static bool IsOutgoing(dynamic fromParsed, string userEmail)
        {
            if (fromParsed is null || (fromParsed is ValueTuple<string, string> tup && tup.Item1 is null))
                return false;
            var addr = ((object)fromParsed) is ValueTuple<string, string> tupFromParse ? tupFromParse.Item1 : ((string)fromParsed);
            return string.Equals(addr?.ToString(), userEmail, StringComparison.OrdinalIgnoreCase);
        }

        public static string DetermineClientEmail(bool isOutgoing, dynamic parsed, string userEmail)
        {
            if (!isOutgoing)
            {
                var from = parsed.From;
                return from?.Item1 ?? "";
            }
            // outgoing: pick first non-user To or Cc
            var tos = parsed.To as List<(string Address, string Name)>;
            var cc = parsed.Cc as List<(string Address, string Name)>;
            var combined = (tos ?? Enumerable.Empty<(string, string)>()).Concat(cc ?? Enumerable.Empty<(string, string)>());
            var candidate = combined.FirstOrDefault(x => !string.Equals(x.Item1, userEmail, StringComparison.OrdinalIgnoreCase));
            return candidate.Item1 ?? combined.FirstOrDefault().Item1 ?? "";
        }
    }

    public static class TextCleaner
    {
        public static string CleanSnippet(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            // Heuristics: remove common reply markers
            var lines = raw.Split('\n');
            var keep = new List<string>();
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (t.StartsWith(">")) break; // quoted section
                if (t.StartsWith("On ") && t.Contains(" wrote:")) break;
                if (t == "--" || t.StartsWith("Thanks") || t.StartsWith("Regards") || t.StartsWith("Sent from my")) break; // signature start
                keep.Add(l);
            }
            return string.Join('\n', keep).Trim();
        }
    }
}
