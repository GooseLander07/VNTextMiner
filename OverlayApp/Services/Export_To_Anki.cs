using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverlayApp.Services
{
    public static class Export_To_Anki
    {
        public static string GenerateGlossaryHtml(JToken? rawData)
        {
            if (rawData == null) return "";

            var sb = new StringBuilder();
            // Yomitan uses an Ordered List for the main senses
            sb.Append("<ol>");

            // 1. Find all "sense-group" nodes recursively
            var senseGroups = new List<JToken>();
            FindSenseGroups(rawData, senseGroups);

            foreach (var group in senseGroups)
            {
                sb.Append("<li>");

                // --- A. Extract Tags (part-of-speech-info) ---
                // These are badges like [noun], [vk]
                var tags = new List<string>();
                FindTags(group, tags);

                if (tags.Count > 0)
                {
                    // Style matching default Yomitan/Anki dark mode
                    sb.Append($"<span style=\"font-size: 0.85em; background: #555; color: #eee; border-radius: 3px; padding: 1px 5px; margin-right: 5px;\">");
                    sb.Append(string.Join(", ", tags));
                    sb.Append("</span>");
                }

                // --- B. Extract Definitions (glossary) ---
                var defs = new List<string>();
                FindDefinitions(group, defs);

                // Join definitions with semicolons (Yomitan default style)
                if (defs.Count > 0)
                {
                    sb.Append(string.Join("; ", defs));
                }

                // --- C. Extract Examples (example-sentence) ---
                var examples = new List<(string jp, string en)>();
                FindExamples(group, examples);

                if (examples.Count > 0)
                {
                    sb.Append("<ul style=\"list-style-type: circle; margin: 5px 0 0 0; padding-left: 20px; color: #ccc;\">");
                    // Limit to 1 example to save space, or remove .Take(1) for all
                    foreach (var ex in examples.Take(1))
                    {
                        sb.Append("<li>");
                        sb.Append($"{ex.jp}");
                        if (!string.IsNullOrEmpty(ex.en)) sb.Append($" <span style=\"color: #888; font-size: 0.9em;\">({ex.en})</span>");
                        sb.Append("</li>");
                    }
                    sb.Append("</ul>");
                }

                sb.Append("</li>");
            }

            sb.Append("</ol>");
            return sb.ToString();
        }

        // Recursive walker to find "sense-group" divs
        private static void FindSenseGroups(JToken token, List<JToken> results)
        {
            if (token is JArray arr) { foreach (var c in arr) FindSenseGroups(c, results); return; }
            if (token is not JObject obj) return;

            // Check if this object is a sense-group wrapper
            string type = obj["data"]?["content"]?.ToString() ?? "";
            if (type == "sense-group")
            {
                results.Add(obj);
                // Don't recurse inside a sense-group looking for other sense-groups
                // (unless you want nested senses, but usually we want the top level)
                return;
            }

            // Recurse
            if (obj["content"] != null) FindSenseGroups(obj["content"], results);
        }

        // Look for "part-of-speech-info" inside a specific group
        private static void FindTags(JToken token, List<string> tags)
        {
            if (token is JArray arr) { foreach (var c in arr) FindTags(c, tags); return; }
            if (token is not JObject obj) return;

            string type = obj["data"]?["content"]?.ToString() ?? "";
            if (type == "part-of-speech-info")
            {
                tags.Add(GetPlainString(obj["content"]));
            }
            else if (obj["content"] != null)
            {
                FindTags(obj["content"], tags);
            }
        }

        // Look for "glossary" inside a specific group
        private static void FindDefinitions(JToken token, List<string> defs)
        {
            if (token is JArray arr) { foreach (var c in arr) FindDefinitions(c, defs); return; }
            if (token is not JObject obj) return;

            string type = obj["data"]?["content"]?.ToString() ?? "";

            if (type == "glossary")
            {
                // Glossaries contain <li> items usually
                if (obj["content"] is JArray items)
                {
                    foreach (var item in items)
                    {
                        // Check if it's an LI tag or just text
                        string text = GetPlainString(item);
                        if (!string.IsNullOrWhiteSpace(text)) defs.Add(text);
                    }
                }
                else
                {
                    string text = GetPlainString(obj["content"]);
                    if (!string.IsNullOrWhiteSpace(text)) defs.Add(text);
                }
            }
            else if (obj["content"] != null)
            {
                FindDefinitions(obj["content"], defs);
            }
        }

        private static void FindExamples(JToken token, List<(string, string)> examples)
        {
            if (token is JArray arr) { foreach (var c in arr) FindExamples(c, examples); return; }
            if (token is not JObject obj) return;

            string type = obj["data"]?["content"]?.ToString() ?? "";

            if (type == "example-sentence")
            {
                string jp = "";
                string en = "";

                // Examples usually have two parts: A (JP) and B (EN)
                if (obj["content"] is JArray parts)
                {
                    foreach (var p in parts)
                    {
                        string pType = p["data"]?["content"]?.ToString() ?? "";
                        if (pType == "example-sentence-a") jp = GetPlainString(p);
                        if (pType == "example-sentence-b") en = GetPlainString(p);
                    }
                }

                if (!string.IsNullOrEmpty(jp)) examples.Add((jp, en));
            }
            else if (obj["content"] != null)
            {
                FindExamples(obj["content"], examples);
            }
        }

        public static string GetPlainString(JToken? token)
        {
            if (token == null) return "";
            if (token.Type == JTokenType.String) return token.ToString();
            if (token is JArray arr) return string.Join("", arr.Select(GetPlainString));
            if (token is JObject obj)
            {
                // EXCLUDE Furigana (rt tags) from plain text
                if (obj["tag"]?.ToString() == "rt") return "";
                return GetPlainString(obj["content"]);
            }
            return "";
        }
    }
}