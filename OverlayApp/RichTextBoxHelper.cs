using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OverlayApp
{
    public class RichTextBoxHelper
    {
        // This registers a new property that allows binding
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached(
                "Document",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(null, OnDocumentChanged));

        // Getter
        public static FlowDocument GetDocument(DependencyObject obj)
        {
            return (FlowDocument)obj.GetValue(DocumentProperty);
        }

        // Setter
        public static void SetDocument(DependencyObject obj, FlowDocument value)
        {
            obj.SetValue(DocumentProperty, value);
        }

        // What to do when the bound data changes
        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBox rtb)
            {
                // If the new value is null, give it an empty doc to prevent crashing
                rtb.Document = (e.NewValue as FlowDocument) ?? new FlowDocument();
            }
        }
    }
}