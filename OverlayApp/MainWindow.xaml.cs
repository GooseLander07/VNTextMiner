using OverlayApp.Models;
using OverlayApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverlayApp
{
    public class FieldMapViewModel
    {
        public string FieldName { get; set; } = "";
        public string SelectedToken { get; set; } = "";
        public List<string> AvailableTokens { get; } = new List<string>
        {
            "", "{expression}", "{reading}", "{furigana}", "{furigana-plain}",
            "{glossary}", "{glossary-brief}", "{sentence}", "{sentence-furigana}",
            "{screenshot}", "{clipboard-text}", "{url}", "{pitch-accents}",
            "{frequencies}", "{part-of-speech}", "{audio}"
        };
    }

    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private TextAnalyzer? _analyzer;
        private DictionaryService? _dictService;
        private AnkiService _ankiService = new AnkiService();
        private IntPtr _windowHandle;
        private IntPtr _nextClipboardViewer;
        private string _lastClipboardText = "";
        private bool _isPaused = false;
        private IntPtr _targetGameHandle = IntPtr.Zero;
        private DispatcherTimer _clipboardPoller;
        public ObservableCollection<FieldMapViewModel> CurrentMappings { get; set; } = new ObservableCollection<FieldMapViewModel>();
        const int WM_DRAWCLIPBOARD = 0x0308;
        const int WM_CHANGECBCHAIN = 0x0309;
        [DllImport("User32.dll")] private static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);
        [DllImport("User32.dll")] private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public MainWindow()
        {
            InitializeComponent();
            ConfigService.Load();
            OpacitySlider.Value = ConfigService.Current.Opacity;
            ChkPause.IsChecked = ConfigService.Current.IsPaused;
            _isPaused = ConfigService.Current.IsPaused;
            this.Top = ConfigService.Current.WindowTop;
            this.Left = ConfigService.Current.WindowLeft;
            this.Width = ConfigService.Current.WindowWidth;
            this.Height = ConfigService.Current.WindowHeight;
            FieldMappingControl.ItemsSource = CurrentMappings;
            Task.Run(() => { try { _analyzer = new TextAnalyzer(); _dictService = new DictionaryService(); } catch { } });
            _clipboardPoller = new DispatcherTimer();
            _clipboardPoller.Interval = TimeSpan.FromMilliseconds(500);
            _clipboardPoller.Tick += (s, e) => CheckClipboard();
            _clipboardPoller.Start();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            OpacitySlider_ValueChanged(OpacitySlider, new RoutedPropertyChangedEventArgs<double>(0, OpacitySlider.Value));
            await LoadAppData();
            if (_dictService != null)
            {
                _dictService.StatusUpdate += (msg) => Dispatcher.Invoke(() => StatusText.Text = msg);
                try { await _dictService.InitializeAsync("jitendex.zip"); StatusText.Text = _isPaused ? "Paused" : "Ready"; }
                catch { StatusText.Text = "Dict Load Error"; }
            }
            _windowHandle = new WindowInteropHelper(this).Handle;
            _nextClipboardViewer = SetClipboardViewer(_windowHandle);
            HwndSource.FromHwnd(_windowHandle)?.AddHook(WndProc);
        }
        private async Task LoadAppData()
        {
            var windows = WindowFactory.GetOpenWindows(); CmbWindows.ItemsSource = windows;
            var decks = await _ankiService.GetDeckNames(); CmbDecks.ItemsSource = decks;
            if (!string.IsNullOrEmpty(ConfigService.Current.AnkiDeck) && decks.Contains(ConfigService.Current.AnkiDeck)) CmbDecks.SelectedItem = ConfigService.Current.AnkiDeck; else if (decks.Count > 0) CmbDecks.SelectedIndex = 0;
            var models = await _ankiService.GetModelNames(); CmbModels.ItemsSource = models;
            if (!string.IsNullOrEmpty(ConfigService.Current.AnkiModel) && models.Contains(ConfigService.Current.AnkiModel)) CmbModels.SelectedItem = ConfigService.Current.AnkiModel; else if (models.Count > 0) CmbModels.SelectedIndex = 0;
        }
        private async void RefreshLists_Click(object sender, RoutedEventArgs e) { await LoadAppData(); MessageBox.Show("Refreshed!"); }
        private void CheckClipboard()
        {
            if (_isPaused) return;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboardText)
                        {
                            _lastClipboardText = text; StatusText.Text = "Analyzing...";
                            Task.Run(() => { if (_analyzer == null) return; var tokens = _analyzer.Analyze(text); Dispatcher.Invoke(() => { TextContainer.ItemsSource = tokens; StatusText.Text = "Ready"; }); });
                        }
                        return;
                    }
                }
                catch (ExternalException) { Thread.Sleep(20); }
                catch { return; }
            }
        }

        private async void Word_Click(object sender, RoutedEventArgs e)
        {
            if (_dictService == null || !_dictService.IsLoaded) return;

            if (sender is Button btn && btn.Tag is Token token)
            {
                if (!token.IsWord) return;

                try
                {
                    // 1. Fetch Data (Background)
                    var entries = await Task.Run(() =>
                    {
                        var res = _dictService.Lookup(token.OriginalForm);
                        if (res.Count == 0) res = _dictService.Lookup(token.Surface);
                        return res;
                    });

                    if (entries.Count == 0) return;

                    // 2. SAFETY: Initialize Docs
                    foreach (var entry in entries)
                    {
                        entry.DefinitionDocument = CreateSafeDoc("Loading...");
                    }

                    // 3. Render First Item
                    entries[0].DefinitionDocument = JitendexParser.ParseToFlowDocument(entries[0].RawDefinition, entries[0].Tags);

                    // 4. Show Popup
                    PopEntries.ItemsSource = entries;
                    DictPopup.IsOpen = true;

                    // 5. --- SMART ANKI CHECK (RESTORED) ---
                    var currentDeck = ConfigService.Current.AnkiDeck;
                    if (!string.IsNullOrEmpty(currentDeck))
                    {
                        // Calculate field name once
                        var wordFieldMap = CurrentMappings.FirstOrDefault(m => m.SelectedToken == "{expression}");
                        string wordFieldName = wordFieldMap?.FieldName ?? "Word";

                        // Fire and Forget background task
                        _ = Task.Run(async () =>
                        {
                            foreach (var entry in entries)
                            {
                                var ids = await _ankiService.FindNotes(currentDeck, wordFieldName, entry.Headword);
                                if (ids.Count > 0)
                                {
                                    // Update UI Thread
                                    Dispatcher.Invoke(() => entry.IsKnown = true);
                                }
                            }
                        });
                    }

                    // 6. Lazy Render the rest
                    if (entries.Count > 1)
                    {
                        await Task.Delay(50);
                        foreach (var entry in entries.Skip(1))
                        {
                            var doc = JitendexParser.ParseToFlowDocument(entry.RawDefinition, entry.Tags);
                            entry.DefinitionDocument = doc;
                            await Task.Yield();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        // Helper (Keep this)
        private FlowDocument CreateSafeDoc(string text)
        {
            var p = new Paragraph(new Run(text)) { Foreground = Brushes.Gray, FontStyle = FontStyles.Italic };
            return new FlowDocument(p) { PagePadding = new Thickness(0), Background = Brushes.Transparent };
        }


        private async void AddSpecificEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not DictionaryEntry entry) return;

            try
            {
                btn.Content = "Adding...";
                btn.IsEnabled = false;

                // 1. PARSE GLOSSARY
                string glossaryHtml = "";
                try
                {
                    glossaryHtml = Export_To_Anki.GenerateGlossaryHtml(entry.RawDefinition);
                }
                catch (Exception ex) { throw new Exception($"Glossary Parse Error: {ex.Message}"); }

                // 2. LAZY LOAD SENSES (For Tags)
                if (entry.Senses == null || entry.Senses.Count == 0)
                {
                    if (_dictService != null)
                        entry.Senses = _dictService.ExtractSenses(entry.RawDefinition);
                }

                // 3. SCREENSHOT (Optimized)
                string screenshotHtml = "";
                if (CurrentMappings.Any(m => m.SelectedToken == "{screenshot}"))
                {
                    try
                    {
                        // Capture
                        System.Drawing.Bitmap? bmp = null;
                        if (_targetGameHandle != IntPtr.Zero)
                            bmp = WindowFactory.CaptureWindow(_targetGameHandle);

                        if (bmp == null) // Fallback
                        {
                            // Manual screen capture logic to get Bitmap object instead of base64 string directly
                            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                            bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                            {
                                g.CopyFromScreen(System.Drawing.Point.Empty, System.Drawing.Point.Empty, bounds.Size);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Screenshot failed: " + ex.Message);
                        // Don't fail the card just because image failed
                    }
                }

                // 4. FIELDS
                string sentenceHtml = _lastClipboardText.Replace(entry.Headword, $"<b>{entry.Headword}</b>");
                string furiganaHtml = (entry.Headword == entry.Reading)
                    ? entry.Headword
                    : $"<ruby>{entry.Headword}<rt>{entry.Reading}</rt></ruby>";

                var fields = new Dictionary<string, object>();
                foreach (var map in CurrentMappings)
                {
                    if (string.IsNullOrEmpty(map.SelectedToken)) continue;

                    string val = "";
                    switch (map.SelectedToken)
                    {
                        case "{expression}": val = entry.Headword; break;
                        case "{reading}": val = entry.Reading; break;
                        case "{furigana}": val = furiganaHtml; break;
                        case "{furigana-plain}": val = $"{entry.Headword}[{entry.Reading}]"; break;
                        case "{glossary}": val = glossaryHtml; break;
                        case "{sentence}": val = sentenceHtml; break;
                        case "{sentence-furigana}": val = sentenceHtml; break;
                        case "{screenshot}": val = screenshotHtml; break;
                        case "{clipboard-text}": val = _lastClipboardText; break;
                        case "{url}": val = "VNTextMiner"; break;
                        case "{part-of-speech}":
                            val = (entry.Tags != null && entry.Tags.Count > 0) ? string.Join(", ", entry.Tags) : "";
                            break;
                    }
                    fields[map.FieldName] = val;
                }

                string deck = CmbDecks.Text;
                string model = CmbModels.Text;

                if (string.IsNullOrEmpty(deck) || string.IsNullOrEmpty(model))
                    throw new Exception("Check Settings: Deck or Model not selected.");

                // 5. SEND
                string result = await _ankiService.AddNote(deck, model, fields, new List<string> { "VN_Mining" });

                if (!result.Contains("Error"))
                {
                    btn.Content = "Saved ✓";
                    btn.Background = new SolidColorBrush(Color.FromRgb(67, 160, 71));
                    btn.Foreground = Brushes.White;
                    entry.IsKnown = true;
                }
                else
                {
                    throw new Exception(result);
                }
            }
            catch (Exception ex)
            {
                btn.Content = "Failed";
                btn.Background = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                btn.IsEnabled = true;
                MessageBox.Show($"Error: {ex.Message}", "Mining Failed");
            }
        }


        // --- GLOSSARY GENERATOR ---
        private string GenerateGlossaryHtml(DictionaryEntry entry)
        {
            var sb = new System.Text.StringBuilder();

            // Standard ordered list. 
            // Anki's default CSS usually handles <ol> correctly.
            sb.Append("<ol>");

            foreach (var sense in entry.Senses)
            {
                sb.Append("<li>");

                // 1. Tags: Minimal styling (Grey badge) just to separate it from text.
                if (sense.PoSTags.Count > 0)
                {
                    // We use inline style here because tags MUST look different from definitions to be readable.
                    // Using standard hex colors (grey) that work on both light/dark mode.
                    sb.Append("<span style=\"font-size: 0.85em; background: #555; color: #eee; border-radius: 3px; padding: 0 4px; margin-right: 5px;\">");
                    sb.Append(string.Join(", ", sense.PoSTags));
                    sb.Append("</span>");
                }

                // 2. Definitions
                // Filter duplicates and empties
                var defs = sense.Glossaries.Distinct().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                sb.Append(string.Join("; ", defs));

                // 3. Examples (if any, just appended cleanly)
                if (sense.Examples.Count > 0)
                {
                    sb.Append("<ul style=\"list-style-type: circle; margin: 5px 0 0 0; padding-left: 20px; color: #888;\">");
                    foreach (var ex in sense.Examples.Take(1))
                    {
                        sb.Append($"<li>{ex.Japanese} ({ex.English})</li>");
                    }
                    sb.Append("</ul>");
                }

                sb.Append("</li>");
            }

            sb.Append("</ol>");
            return sb.ToString();
        }




        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { DictPopup.IsOpen = false; try { this.DragMove(); } catch { } } }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        protected override void OnClosing(CancelEventArgs e) { ConfigService.Current.WindowTop = this.Top; ConfigService.Current.WindowLeft = this.Left; ConfigService.Current.WindowWidth = this.ActualWidth; ConfigService.Current.WindowHeight = this.ActualHeight; ConfigService.Save(); base.OnClosing(e); }
        private void Window_Closed(object sender, EventArgs e) => ChangeClipboardChain(_windowHandle, _nextClipboardViewer);
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { if (msg == WM_DRAWCLIPBOARD) { CheckClipboard(); SendMessage(_nextClipboardViewer, msg, wParam, lParam); } else if (msg == WM_CHANGECBCHAIN) { if (wParam == _nextClipboardViewer) _nextClipboardViewer = lParam; else SendMessage(_nextClipboardViewer, msg, wParam, lParam); } return IntPtr.Zero; }
        private void Popup_PreviewMouseWheel(object sender, MouseWheelEventArgs e) { DictScrollViewer.ScrollToVerticalOffset(DictScrollViewer.VerticalOffset - e.Delta); e.Handled = true; }
        private async void TestAnki_Click(object sender, RoutedEventArgs e) { bool c = await _ankiService.CheckConnection(); AnkiStatus.Text = c ? "Connected" : "Failed"; AnkiStatus.Foreground = c ? Brushes.LightGreen : Brushes.Red; if (c) await LoadAppData(); }
        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        private void ChkPause_Checked(object sender, RoutedEventArgs e) { _isPaused = ChkPause.IsChecked ?? false; StatusText.Text = _isPaused ? "Paused" : "Ready"; ConfigService.Current.IsPaused = _isPaused; }
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { this.Background = new SolidColorBrush(Color.FromArgb((byte)(e.NewValue * 255), 30, 30, 30)); ConfigService.Current.Opacity = e.NewValue; }
        private void CmbDecks_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CmbDecks.SelectedItem is string s) ConfigService.Current.AnkiDeck = s; else ConfigService.Current.AnkiDeck = CmbDecks.Text; }
        private async void CmbModels_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CmbModels.SelectedItem is string modelName) { ConfigService.Current.AnkiModel = modelName; await UpdateFieldMappings(modelName); } }
        private void CmbWindows_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CmbWindows.SelectedItem is WindowInfo info) _targetGameHandle = info.Handle; }
        private async Task UpdateFieldMappings(string modelName) { CurrentMappings.Clear(); var fields = await _ankiService.GetModelFields(modelName); Dictionary<string, string> savedMap = new Dictionary<string, string>(); if (ConfigService.Current.SavedMappings.ContainsKey(modelName)) savedMap = ConfigService.Current.SavedMappings[modelName]; foreach (var field in fields) { string token = ""; if (savedMap.ContainsKey(field)) token = savedMap[field]; else { string f = field.ToLower(); if (f.Contains("word") || f.Contains("expression") || f.Contains("kanji")) token = "{expression}"; else if (f.Contains("reading") || f.Contains("kana")) token = "{reading}"; else if (f.Contains("sentence") || f.Contains("context")) token = "{sentence}"; else if (f.Contains("meaning") || f.Contains("glossary") || f.Contains("def")) token = "{glossary}"; else if (f.Contains("pic") || f.Contains("image") || f.Contains("screen")) token = "{screenshot}"; else if (f.Contains("audio") || f.Contains("sound")) token = "{audio}"; else if (f.Contains("pitch")) token = "{pitch-accents}"; else if (f.Contains("freq")) token = "{frequencies}"; else if (f.Contains("furigana")) token = "{furigana}"; } CurrentMappings.Add(new FieldMapViewModel { FieldName = field, SelectedToken = token }); } }
        private void MappingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { string model = ConfigService.Current.AnkiModel; if (string.IsNullOrEmpty(model)) return; var map = new Dictionary<string, string>(); foreach (var vm in CurrentMappings) map[vm.FieldName] = vm.SelectedToken; ConfigService.Current.SavedMappings[model] = map; }
    }
}