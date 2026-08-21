#nullable enable

using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SynToolkit.Views
{
    public static class LocalizedText
    {
        public static readonly DependencyProperty TextKeyProperty = CreateProperty("TextKey", ApplyText);
        public static readonly DependencyProperty ContentKeyProperty = CreateProperty("ContentKey", ApplyContent);
        public static readonly DependencyProperty HeaderKeyProperty = CreateProperty("HeaderKey", ApplyHeader);
        public static readonly DependencyProperty DescriptionKeyProperty = CreateProperty("DescriptionKey", ApplyDescription);
        public static readonly DependencyProperty TitleKeyProperty = CreateProperty("TitleKey", ApplyTitle);
        public static readonly DependencyProperty MessageKeyProperty = CreateProperty("MessageKey", ApplyMessage);
        public static readonly DependencyProperty PlaceholderTextKeyProperty = CreateProperty("PlaceholderTextKey", ApplyPlaceholderText);

        public static void SetTextKey(DependencyObject element, string value) => element.SetValue(TextKeyProperty, value);
        public static string GetTextKey(DependencyObject element) => (string)element.GetValue(TextKeyProperty);
        public static void SetContentKey(DependencyObject element, string value) => element.SetValue(ContentKeyProperty, value);
        public static string GetContentKey(DependencyObject element) => (string)element.GetValue(ContentKeyProperty);
        public static void SetHeaderKey(DependencyObject element, string value) => element.SetValue(HeaderKeyProperty, value);
        public static string GetHeaderKey(DependencyObject element) => (string)element.GetValue(HeaderKeyProperty);
        public static void SetDescriptionKey(DependencyObject element, string value) => element.SetValue(DescriptionKeyProperty, value);
        public static string GetDescriptionKey(DependencyObject element) => (string)element.GetValue(DescriptionKeyProperty);
        public static void SetTitleKey(DependencyObject element, string value) => element.SetValue(TitleKeyProperty, value);
        public static string GetTitleKey(DependencyObject element) => (string)element.GetValue(TitleKeyProperty);
        public static void SetMessageKey(DependencyObject element, string value) => element.SetValue(MessageKeyProperty, value);
        public static string GetMessageKey(DependencyObject element) => (string)element.GetValue(MessageKeyProperty);
        public static void SetPlaceholderTextKey(DependencyObject element, string value) => element.SetValue(PlaceholderTextKeyProperty, value);
        public static string GetPlaceholderTextKey(DependencyObject element) => (string)element.GetValue(PlaceholderTextKeyProperty);

        private static DependencyProperty CreateProperty(string name, PropertyChangedCallback callback) =>
            DependencyProperty.RegisterAttached(name, typeof(string), typeof(LocalizedText), new PropertyMetadata(null, callback));

        private static string Text(DependencyPropertyChangedEventArgs args) =>
            args.NewValue is string key ? App.GetValueFromItemList(key) : string.Empty;

        private static void ApplyText(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is TextBlock textBlock) textBlock.Text = Text(args);
        }

        private static void ApplyContent(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is ContentControl control) control.Content = Text(args);
        }

        private static void ApplyHeader(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is SettingsCard card) card.Header = Text(args);
            if (sender is SettingsExpander expander) expander.Header = Text(args);
        }

        private static void ApplyDescription(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is SettingsCard card) card.Description = Text(args);
            if (sender is SettingsExpander expander) expander.Description = Text(args);
        }

        private static void ApplyTitle(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is InfoBar infoBar) infoBar.Title = Text(args);
        }

        private static void ApplyMessage(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is InfoBar infoBar) infoBar.Message = Text(args);
        }

        private static void ApplyPlaceholderText(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is AutoSuggestBox autoSuggestBox) autoSuggestBox.PlaceholderText = Text(args);
            if (sender is TextBox textBox) textBox.PlaceholderText = Text(args);
        }
    }
}