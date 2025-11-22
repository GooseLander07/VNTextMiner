using System.Collections.Generic;

namespace OverlayApp.Models
{
    public class AppConfig
    {
        public string AnkiDeck { get; set; } = "";
        public string AnkiModel { get; set; } = "";

        // Stores mappings: Key = ModelName, Value = Dictionary<FieldName, TokenName>
        public Dictionary<string, Dictionary<string, string>> SavedMappings { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        public double Opacity { get; set; } = 0.8;
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public double WindowWidth { get; set; } = 800;
        public double WindowHeight { get; set; } = 150;
        public bool IsPaused { get; set; } = false;
    }
}