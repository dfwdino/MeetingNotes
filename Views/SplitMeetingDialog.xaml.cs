using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace MeetingNotes.Views;

public partial class SplitMeetingDialog : Window
{
    private readonly string[] _rawLines;

    public int SplitLineIndex { get; private set; } = -1;
    public string NewTitle => TitleBox.Text.Trim();

    public SplitMeetingDialog(string transcript, string currentTitle)
    {
        InitializeComponent();
        _rawLines = transcript.Split('\n');

        for (int i = 0; i < _rawLines.Length; i++)
        {
            var line = _rawLines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            bool isTimestamp = line.TrimStart().StartsWith('[');

            var item = new ListBoxItem
            {
                Tag     = i,
                Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock
                {
                    Text          = line,
                    TextTrimming  = TextTrimming.CharacterEllipsis,
                    Foreground    = isTimestamp
                        ? new SolidColorBrush(WpfColor.FromRgb(100, 180, 255))
                        : new SolidColorBrush(WpfColor.FromRgb(200, 200, 200))
                }
            };

            LinesList.Items.Add(item);
        }

        TitleBox.Text = currentTitle + " (continued)";
        Loaded += (_, _) => LinesList.Focus();
    }

    private void LinesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LinesList.SelectedItem is not ListBoxItem item || item.Tag is not int idx) return;

        SplitLineIndex = idx;
        var above = LinesList.SelectedIndex;
        var below = LinesList.Items.Count - LinesList.SelectedIndex;
        SelectionInfo.Text =
            $"{above} line{(above == 1 ? "" : "s")} stay in current meeting  ·  " +
            $"{below} line{(below == 1 ? "" : "s")} move to new meeting";
        SplitButton.IsEnabled = true;
    }

    private void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (SplitLineIndex < 0 || string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SplitButton_Click(sender, new RoutedEventArgs());
        if (e.Key == Key.Escape) CancelButton_Click(sender, new RoutedEventArgs());
    }
}
