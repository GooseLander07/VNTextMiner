using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OverlayApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OverlayApp.Services
{
    public class DictionaryService
    {
        private const string DB_NAME = "dict.db";
        // Bumped to 12 to force re-indexing
        private const int DB_VERSION = 12;
        public bool IsLoaded { get; private set; } = false;
        public event Action<string>? StatusUpdate;

        public async Task InitializeAsync(string zipPath)
        {
            if (File.Exists(DB_NAME))
            {
                if (await IsDatabaseOutdated())
                {
                    StatusUpdate?.Invoke("Updating dictionary...");
                    SqliteConnection.ClearAllPools();
                    File.Delete(DB_NAME);
                }
                else
                {
                    StatusUpdate?.Invoke("Ready.");
                    IsLoaded = true;
                    return;
                }
            }

            StatusUpdate?.Invoke("Creating Database (First time only)...");
            await Task.Run(() =>
            {
                try
                {
                    using (var connection = new SqliteConnection($"Data Source={DB_NAME}"))
                    {
                        connection.Open();
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = @"
                                CREATE TABLE IF NOT EXISTS entries (term TEXT, reading TEXT, score INTEGER, json_data TEXT);
                                CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
                                CREATE INDEX IF NOT EXISTS idx_term ON entries(term);
                                CREATE INDEX IF NOT EXISTS idx_reading ON entries(reading);
                                CREATE INDEX IF NOT EXISTS idx_score ON entries(score);
                            ";
                            cmd.ExecuteNonQuery();
                        }

                        using (var transaction = connection.BeginTransaction())
                        {
                            using (FileStream fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
                            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                int count = 0;
                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.Name.StartsWith("term_bank") && entry.Name.EndsWith(".json"))
                                    {
                                        ProcessJsonFile(entry, connection, transaction);
                                        count++;
                                        if (count % 10 == 0) StatusUpdate?.Invoke($"Importing bank {count}...");
                                    }
                                }
                            }
                            var vCmd = connection.CreateCommand();
                            vCmd.Transaction = transaction;
                            vCmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ('version', $v)";
                            vCmd.Parameters.AddWithValue("$v", DB_VERSION);
                            vCmd.ExecuteNonQuery();
                            transaction.Commit();
                        }
                    }
                    IsLoaded = true;
                }
                catch (Exception ex) { StatusUpdate?.Invoke("Error: " + ex.Message); }
            });
            StatusUpdate?.Invoke("Ready.");
        }

        private async Task<bool> IsDatabaseOutdated()
        {
            try
            {
                using (var connection = new SqliteConnection($"Data Source={DB_NAME}"))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
                    var result = await cmd.ExecuteScalarAsync();
                    return result == null || (int.TryParse(result.ToString(), out int v) && v < DB_VERSION);
                }
            }
            catch { return true; }
        }

        private void ProcessJsonFile(ZipArchiveEntry entry, SqliteConnection conn, SqliteTransaction trans)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(reader))
            {
                var termBank = JArray.Load(jsonReader);
                var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = "INSERT INTO entries (term, reading, score, json_data) VALUES ($t, $r, $s, $j)";
                var pTerm = cmd.CreateParameter(); pTerm.ParameterName = "$t"; cmd.Parameters.Add(pTerm);
                var pRead = cmd.CreateParameter(); pRead.ParameterName = "$r"; cmd.Parameters.Add(pRead);
                var pScore = cmd.CreateParameter(); pScore.ParameterName = "$s"; cmd.Parameters.Add(pScore);
                var pJson = cmd.CreateParameter(); pJson.ParameterName = "$j"; cmd.Parameters.Add(pJson);

                foreach (var item in termBank)
                {
                    if (item is not JArray arr || arr.Count < 6) continue;
                    pTerm.Value = arr[0].ToString();
                    pRead.Value = arr[1].ToString();
                    int.TryParse(arr[4].ToString(), out int score);
                    pScore.Value = score;

                    var storageObj = new { raw = arr[5], tags = arr[2] };
                    pJson.Value = JsonConvert.SerializeObject(storageObj);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<DictionaryEntry> Lookup(string word)
        {
            var list = new List<DictionaryEntry>();
            if (!IsLoaded) return list;

            using (var conn = new SqliteConnection($"Data Source={DB_NAME}"))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                // Indices make this query instant
                cmd.CommandText = "SELECT term, reading, json_data FROM entries WHERE term=$w OR reading=$w ORDER BY score DESC LIMIT 5";
                cmd.Parameters.AddWithValue("$w", word);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var e = new DictionaryEntry { Headword = r.GetString(0), Reading = r.GetString(1) };
                        try
                        {
                            var json = JObject.Parse(r.GetString(2));
                            e.RawDefinition = json["raw"];
                            e.Tags = json["tags"]?.ToObject<List<string>>() ?? new List<string>();
                        }
                        catch { }
                        list.Add(e);
                    }
                }
            }
            return list;
        }

        public List<Sense> ExtractSenses(JToken? rawToken)
        {
            var senses = new List<Sense>();
            if (rawToken == null) return senses;
            Walk(rawToken, senses);
            return senses;
        }

        private void Walk(JToken t, List<Sense> senses)
        {
            if (t is JArray arr) { foreach (var c in arr) Walk(c, senses); return; }
            if (t is not JObject obj) return;

            string type = obj["data"]?["content"]?.ToString() ?? "";

            if (type == "sense-group")
            {
                var newSense = new Sense();
                if (obj["content"] is JArray children)
                {
                    foreach (var c in children)
                    {
                        if (c is JObject cObj && cObj["data"]?["content"]?.ToString() == "part-of-speech-info")
                            newSense.PoSTags.Add(GetPlainString(cObj["content"]));
                    }
                }
                senses.Add(newSense);
                Walk(obj["content"], senses);
            }
            else if (type == "glossary")
            {
                if (senses.Count == 0) senses.Add(new Sense());
                var target = senses.Last();

                if (obj["content"] is JArray items)
                {
                    foreach (var item in items)
                    {
                        var def = GetPlainString(item);
                        if (!string.IsNullOrWhiteSpace(def)) target.Glossaries.Add(def);
                    }
                }
                else
                {
                    var def = GetPlainString(obj["content"]);
                    if (!string.IsNullOrWhiteSpace(def)) target.Glossaries.Add(def);
                }
            }
            else if (type == "example-sentence")
            {
                if (senses.Count == 0) senses.Add(new Sense());
                var ex = new ExampleSentence();
                if (obj["content"] is JArray parts)
                {
                    foreach (var p in parts)
                    {
                        string pt = p["data"]?["content"]?.ToString() ?? "";
                        if (pt == "example-sentence-a") ex.Japanese = GetPlainString(p);
                        if (pt == "example-sentence-b") ex.English = GetPlainString(p);
                    }
                }
                if (!string.IsNullOrWhiteSpace(ex.Japanese)) senses.Last().Examples.Add(ex);
            }
            else if (obj["content"] != null)
            {
                Walk(obj["content"], senses);
            }
        }

        private string GetPlainString(JToken? token)
        {
            if (token == null) return "";
            if (token.Type == JTokenType.String) return token.ToString();
            if (token is JArray arr) return string.Join("", arr.Select(GetPlainString));
            if (token is JObject obj)
            {
                if (obj["tag"]?.ToString() == "rt") return "";
                return GetPlainString(obj["content"]);
            }
            return "";
        }
    }
}