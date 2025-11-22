using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace OverlayApp.Services
{
    public static class JitendexParser
    {
        // Frozen brushes for thread safety
        private static readonly Brush ColorPos = GetFrozenBrush(Color.FromRgb(86, 86, 86));
        private static readonly Brush ColorMisc = GetFrozenBrush(Colors.Brown);
        private static readonly Brush ColorExampleKey = GetFrozenBrush(Colors.LimeGreen);
        private static readonly Brush ColorText = GetFrozenBrush(Colors.WhiteSmoke);

        private static Brush GetFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static FlowDocument ParseToFlowDocument(JToken? definitionsArray, List<string>? topTags)
        {
            if (Application.Current == null) return new FlowDocument();

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = ColorText,
                FontFamily = new FontFamily("Segoe UI, Yu Gothic UI"),
                FontSize = 14
            };

            // 1. Top Tags (Badges)
            if (topTags != null && topTags.Count > 0)
            {
                var tagPara = new Paragraph();
                foreach (var tag in topTags)
                {
                    if (!string.IsNullOrEmpty(tag))
                    {
                        tagPara.Inlines.Add(CreateBadge(tag, ColorPos));
                        tagPara.Inlines.Add(new Run(" "));
                    }
                }
                doc.Blocks.Add(tagPara);
            }

            // 2. Main Content
            if (definitionsArray is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JObject obj && obj["type"]?.ToString() == "structured-content")
                    {
                        // This will now correctly find UL/OL lists inside DIVs
                        var blocks = ParseBlock(obj["content"]);
                        foreach (var b in blocks) doc.Blocks.Add(b);
                    }
                    else if (item.Type == JTokenType.String)
                    {
                        doc.Blocks.Add(new Paragraph(new Run("• " + item.ToString())));
                    }
                }
            }
            return doc;
        }

        private static List<Block> ParseBlock(JToken? token)
        {
            var blocks = new List<Block>();
            if (token == null) return blocks;

            if (token is JArray arr)
            {
                foreach (var child in arr) blocks.AddRange(ParseBlock(child));
            }
            else if (token is JObject obj)
            {
                string? tag = obj["tag"]?.ToString();
                var content = obj["content"];
                var data = obj["data"];

                if (tag == "ul" || tag == "ol")
                {
                    var list = new List();
                    list.MarkerStyle = tag == "ul" ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal;
                    list.Padding = new Thickness(20, 0, 0, 0);

                    var items = new List<JToken>();
                    if (content is JArray a) items.AddRange(a);
                    else if (content is JObject o) items.Add(o);

                    foreach (var li in items)
                    {
                        var listItem = new ListItem();
                        // Recursively parse list items so formatting works inside them
                        var childBlocks = ParseBlock(li);
                        foreach (var b in childBlocks) listItem.Blocks.Add(b);
                        list.ListItems.Add(listItem);
                    }
                    blocks.Add(list);
                }
                else if (tag == "li")
                {
                    // Unwrap LI content to find blocks inside
                    blocks.AddRange(ParseBlock(content));
                }
                else if (tag == "div")
                {
                    if (data?["content"]?.ToString() == "example-sentence")
                    {
                        var p = new Paragraph { Margin = new Thickness(10, 5, 0, 5) };
                        ParseInline(content, p.Inlines);
                        blocks.Add(p);
                    }
                    else
                    {
                        // --- THE FIX ---
                        // Instead of forcing a Paragraph (which mashes text),
                        // we recurse to check if this div contains a List (ul/ol).
                        blocks.AddRange(ParseBlock(content));
                    }
                }
                else
                {
                    // Fallback for spans, pure text, or unknown inline tags
                    var p = new Paragraph();
                    ParseInline(token, p.Inlines);
                    if (p.Inlines.Count > 0) blocks.Add(p);
                }
            }
            else if (token.Type == JTokenType.String)
            {
                // Naked text becomes a paragraph
                blocks.Add(new Paragraph(new Run(token.ToString())));
            }
            return blocks;
        }

        private static void ParseInline(JToken? token, InlineCollection inlines, Brush? inheritedForeground = null)
        {
            if (token == null) return;
            if (token is JArray arr) { foreach (var child in arr) ParseInline(child, inlines, inheritedForeground); return; }

            if (token.Type == JTokenType.String)
            {
                var r = new Run(token.ToString());
                if (inheritedForeground != null) r.Foreground = inheritedForeground;
                inlines.Add(r);
                return;
            }

            if (token is JObject obj)
            {
                string? tag = obj["tag"]?.ToString();
                var content = obj["content"];
                var data = obj["data"];

                if (tag == "span")
                {
                    string? type = data?["content"]?.ToString();
                    if (type == "part-of-speech-info") inlines.Add(CreateBadge(GetContentString(content), ColorPos));
                    else if (type == "misc-info") inlines.Add(CreateBadge(GetContentString(content), ColorMisc));
                    else if (type == "example-keyword")
                    {
                        var s = new Span(); ParseInline(content, s.Inlines, ColorExampleKey); inlines.Add(s);
                    }
                    else
                    {
                        var s = new Span(); ParseInline(content, s.Inlines, inheritedForeground); inlines.Add(s);
                    }
                }
                else if (tag == "ruby")
                {
                    if (content is JArray parts)
                    {
                        var span = new Span();
                        for (int i = 0; i < parts.Count; i++)
                        {
                            var item = parts[i];
                            if (IsRubyTag(item)) continue;
                            string baseText = GetContentString(item);
                            string rtText = "";
                            if (i + 1 < parts.Count && IsRubyTag(parts[i + 1])) rtText = GetContentString(parts[i + 1]["content"]);

                            var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) };
                            if (!string.IsNullOrEmpty(rtText)) stack.Children.Add(new TextBlock { Text = rtText, FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                            stack.Children.Add(new TextBlock { Text = baseText, FontSize = 14, Foreground = inheritedForeground ?? ColorText, HorizontalAlignment = HorizontalAlignment.Center });
                            span.Inlines.Add(new InlineUIContainer(stack) { BaselineAlignment = BaselineAlignment.Bottom });
                        }
                        inlines.Add(span);
                    }
                }
                else if (tag == "a")
                {
                    var link = new Span(); link.Foreground = GetFrozenBrush(Color.FromRgb(100, 181, 246));
                    ParseInline(content, link.Inlines, link.Foreground); inlines.Add(link);
                }
                else ParseInline(content, inlines, inheritedForeground);
            }
        }

        private static bool IsRubyTag(JToken? t) => t is JObject o && o["tag"]?.ToString() == "rt";
        private static string GetContentString(JToken? t) => t?.ToString() ?? "";

        private static InlineUIContainer CreateBadge(string text, Brush bg)
        {
            var b = new Border { Background = bg, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 4, 0) };
            b.Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold };
            return new InlineUIContainer(b) { BaselineAlignment = BaselineAlignment.Center };
        }
    }
}