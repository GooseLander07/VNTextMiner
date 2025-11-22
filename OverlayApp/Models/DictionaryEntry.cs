using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Documents; // FlowDocument
using Newtonsoft.Json.Linq;

namespace OverlayApp.Models
{
    public class DictionaryEntry : INotifyPropertyChanged
    {
        public string Headword { get; set; } = "";
        public string Reading { get; set; } = "";
        public int Score { get; set; } = 0;
        public bool IsPriority => Score >= 1000;

        private FlowDocument? _definitionDocument;
        public FlowDocument? DefinitionDocument
        {
            get => _definitionDocument;
            set { _definitionDocument = value; OnPropertyChanged(); }
        }

        private bool _isKnown = false;
        public bool IsKnown
        {
            get => _isKnown;
            set { _isKnown = value; OnPropertyChanged(); }
        }

        public JToken? RawDefinition { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<Sense> Senses { get; set; } = new List<Sense>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class Sense
    {
        public List<string> PoSTags { get; set; } = new List<string>();
        public List<string> Glossaries { get; set; } = new List<string>();
        public List<string> Info { get; set; } = new List<string>();
        public List<ExampleSentence> Examples { get; set; } = new List<ExampleSentence>();
    }

    public class ExampleSentence
    {
        public string Japanese { get; set; } = "";
        public string English { get; set; } = "";
    }
}