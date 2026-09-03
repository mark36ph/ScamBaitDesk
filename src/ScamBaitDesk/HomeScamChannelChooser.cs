using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScamBaitDesk;

public sealed partial class MainWindow
{
    private bool _channelChooserInitialized;

    private void InitializeChannelChooser()
    {
        if (_channelChooserInitialized) return;
        _channelChooserInitialized = true;

        if (GuideTab?.Content is not ScrollViewer scroll || scroll.Content is not StackPanel stack)
            return;

        var existing = stack.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Child is StackPanel panel &&
                panel.Children.OfType<TextBlock>().Any(text => text.Text == "One workspace · three scam channels"));
        if (existing is not null)
            stack.Children.Remove(existing);

        var chooser = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "What are you investigating?",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose the scam channel first. ScamBait Desk will take you to the right investigation tools and keep the workflow manual and evidence-focused.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var cards = new Grid { ColumnSpacing = 12 };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddChannelCard(cards, 0, "✉", "Email scam", "Review messages, headers, links, indicators and evidence.", "Open email investigation", () =>
        {
            SelectShellDestination("Inbox");
            NavigateShell("Inbox");
        });

        AddChannelCard(cards, 1, "☎", "Phone scam", "Analyse a caller number and transcript for social-engineering signals.", "Open phone investigation", OpenPhoneWorkspace);

        AddChannelCard(cards, 2, "🌐", "Website scam", "Scan a suspicious URL and inspect page, redirect and form indicators safely.", "Open website investigation", () =>
        {
            SelectShellDestination("Website");
            NavigateShell("Website");
        });

        panel.Children.Add(cards);
        chooser.Child = panel;
        stack.Children.Insert(0, chooser);
    }

    private static void AddChannelCard(Grid parent, int column, string icon, string title, string description, string buttonText, Action action)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = icon, FontSize = 28 });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 52,
            Opacity = 0.75
        });
        var button = new Button { Content = buttonText, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action();
        panel.Children.Add(button);

        card.Child = panel;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }
}
