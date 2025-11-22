using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OverlayApp.Services
{
    public class AnkiService
    {
        private const string ANKI_URL = "http://127.0.0.1:8765";
        private readonly HttpClient _client = new HttpClient();

        public async Task<bool> CheckConnection()
        {
            try { return await Post("version") != null; } catch { return false; }
        }

        public async Task<List<string>> GetDeckNames()
        {
            try
            {
                dynamic? result = await Post("deckNames");
                return result?.ToObject<List<string>>() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public async Task<List<string>> GetModelNames()
        {
            try
            {
                dynamic? result = await Post("modelNames");
                return result?.ToObject<List<string>>() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public async Task<List<string>> GetModelFields(string modelName)
        {
            try
            {
                var payload = new { action = "modelFieldNames", version = 6, @params = new { modelName = modelName } };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(ANKI_URL, content);
                var resultString = await response.Content.ReadAsStringAsync();
                var jsonResp = JObject.Parse(resultString);
                return jsonResp["result"]?.ToObject<List<string>>() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public async Task<string?> StorePicture(string filename, string base64Data)
        {
            var payload = new
            {
                action = "storeMediaFile",
                version = 6,
                @params = new { filename = filename, data = base64Data }
            };

            try
            {
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(ANKI_URL, content);
                var result = await response.Content.ReadAsStringAsync();
                if (result.Contains("null")) return null;
                return "Error storing image";
            }
            catch { return "Network Error"; }
        }

        public async Task<string> AddNote(string deckName, string modelName, Dictionary<string, object> fields, List<string> tags)
        {
            var note = new
            {
                deckName = deckName,
                modelName = modelName,
                fields = fields,
                tags = tags,
                // This allows you to add words with the same Kanji but different readings (e.g. 食物)
                options = new { allowDuplicate = true, duplicateScope = "deck" }
            };

            var payload = new { action = "addNote", version = 6, @params = new { note = note } };

            try
            {
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(ANKI_URL, content);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (resultJson.Contains("\"error\": null")) return "Success";

                var obj = JObject.Parse(resultJson);
                return "Anki Error: " + obj["error"]?.ToString();
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        private async Task<dynamic?> Post(string action)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new { action = action, version = 6 });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(ANKI_URL, content);
                var respString = await response.Content.ReadAsStringAsync();
                var jsonResp = JObject.Parse(respString);
                return jsonResp["result"];
            }
            catch { return null; }
        }
        public async Task<List<long>> FindNotes(string deckName, string fieldName, string searchTerm)
        {
            // Query format: "deck:MyDeck" "Field:SearchTerm"
            // We escape quotes just in case the word has them
            string query = $"\"deck:{deckName}\" \"{fieldName}:{searchTerm}\"";

            var payload = new
            {
                action = "findNotes",
                version = 6,
                @params = new { query = query }
            };

            try
            {
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(ANKI_URL, content);
                var resultString = await response.Content.ReadAsStringAsync();

                var jsonResp = JObject.Parse(resultString);
                return jsonResp["result"]?.ToObject<List<long>>() ?? new List<long>();
            }
            catch
            {
                return new List<long>();
            }
        }

    }
}