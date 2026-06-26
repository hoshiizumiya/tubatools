using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Net.Http;
using System.Text.Json;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3;

public sealed partial class WhatsNewWindow : Window
{
    private record ReleaseEntry(string TagName, string? Name, string? Body, string? Date);

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private List<ReleaseEntry> _releases = [];

    static WhatsNewWindow()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-WhatsNew");
    }

    public WhatsNewWindow()
    {
        InitializeComponent();
        InitWindow();
        _ = LoadReleasesAsync();
    }

    private void InitWindow()
    {
        AppWindow.Title = "新增内容";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = Math.Min(920, (int)(screenArea.Width * 0.6));
        var height = Math.Min(660, (int)(screenArea.Height * 0.75));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;
    }

    private async Task LoadReleasesAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var owner = "luolangaga";
            var repo = "tubatool";
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20";

            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _releases.Clear();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var tag = item.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                    var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var body = item.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
                    var date = item.TryGetProperty("published_at", out var dateEl) ? dateEl.GetString() : null;

                    if (!string.IsNullOrEmpty(tag))
                        _releases.Add(new ReleaseEntry(tag, name, body, date));
                }
            }

            PopulateVersionList();

            if (_releases.Count > 0)
                VersionListView.SelectedIndex = 0;
        }
        catch
        {
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateVersionList()
    {
        VersionListView.Items.Clear();

        foreach (var release in _releases)
        {
            var sp = new StackPanel { Spacing = 2 };

            var titleText = new TextBlock
            {
                Text = release.Name ?? release.TagName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var dateText = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (release.Date is not null && DateTime.TryParse(release.Date, out var dt))
                dateText.Text = dt.ToString("yyyy-MM-dd");

            sp.Children.Add(titleText);
            sp.Children.Add(dateText);

            var item = new ListViewItem
            {
                Tag = release.TagName,
                Content = sp
            };

            VersionListView.Items.Add(item);
        }
    }

    private void VersionListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionListView.SelectedIndex < 0 || VersionListView.SelectedIndex >= _releases.Count)
            return;

        var release = _releases[VersionListView.SelectedIndex];
        VersionTitleText.Text = release.Name ?? release.TagName;

        if (release.Date is not null && DateTime.TryParse(release.Date, out var dt))
            VersionDateText.Text = dt.ToString("yyyy年M月d日");
        else
            VersionDateText.Text = "";

        RenderChangelog(release.Body);
    }

    private void RenderChangelog(string? markdown)
    {
        ChangelogPanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            ChangelogPanel.Children.Add(new TextBlock
            {
                Text = "暂无更新日志。",
                Opacity = 0.6,
                FontSize = 14
            });
            return;
        }

        var lines = markdown.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("### "))
            {
                var text = line[4..].Trim();
                ChangelogPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 4)
                });
                i++;
            }
            else if (line.StartsWith("## "))
            {
                var text = line[3..].Trim();
                ChangelogPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Margin = new Thickness(0, 16, 0, 6)
                });
                i++;
            }
            else if (line.StartsWith("# "))
            {
                var text = line[2..].Trim();
                ChangelogPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                i++;
            }
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var listItems = new List<string>();
                while (i < lines.Length)
                {
                    var l = lines[i];
                    if (string.IsNullOrWhiteSpace(l)) { i++; continue; }
                    if (!l.TrimStart().StartsWith("- ") && !l.TrimStart().StartsWith("* ")) break;
                    var content = l.TrimStart()[2..].Trim();
                    listItems.Add(content);
                    i++;
                }

                foreach (var itemText in listItems)
                {
                    var row = new Grid
                    {
                        ColumnSpacing = 8,
                        Padding = new Thickness(8, 2, 0, 2)
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var bullet = new Border
                    {
                        Width = 4,
                        Height = 4,
                        CornerRadius = new CornerRadius(2),
                        Background = (Brush)App.Current.Resources["AccentFillColorDefaultBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var contentBlock = CreateInlineTextBlock(itemText);
                    Grid.SetColumn(bullet, 0);
                    Grid.SetColumn(contentBlock, 1);
                    row.Children.Add(bullet);
                    row.Children.Add(contentBlock);

                    ChangelogPanel.Children.Add(row);
                }
            }
            else if (line.StartsWith("|") && line.Contains("|"))
            {
                var tableRows = new List<string[]>();
                while (i < lines.Length)
                {
                    var l = lines[i];
                    if (!l.StartsWith("|")) break;
                    if (l.Replace("|", "").Replace("-", "").Replace(" ", "").Length == 0)
                    {
                        i++;
                        continue;
                    }
                    var cells = l.Split('|')
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c.Trim())
                        .ToArray();
                    tableRows.Add(cells);
                    i++;
                }

                if (tableRows.Count > 0)
                {
                    var colCount = tableRows[0].Length;
                    var tableGrid = new Grid
                    {
                        CornerRadius = new CornerRadius(6),
                        Background = (Brush)App.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                        BorderBrush = (Brush)App.Current.Resources["CardStrokeColorDefaultBrush"],
                        BorderThickness = new Thickness(1)
                    };

                    for (var c = 0; c < colCount; c++)
                        tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    for (var r = 0; r < tableRows.Count; r++)
                    {
                        var isHeader = r == 0;
                        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        for (var c = 0; c < tableRows[r].Length && c < colCount; c++)
                        {
                            var cellText = tableRows[r][c];
                            var cellBlock = new TextBlock
                            {
                                Text = StripMarkdownFormatting(cellText),
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                                Padding = new Thickness(10, 6, 10, 6)
                            };
                            if (isHeader)
                            {
                                cellBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                                cellBlock.Opacity = 0.85;
                            }

                            var cellBorder = new Border
                            {
                                BorderBrush = (Brush)App.Current.Resources["DividerStrokeColorDefaultBrush"],
                                BorderThickness = new Thickness(c > 0 ? 1 : 0, r > 0 ? 1 : 0, 0, 0),
                                Child = cellBlock
                            };
                            if (isHeader)
                            {
                                cellBorder.Background = (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
                            }

                            Grid.SetRow(cellBorder, r);
                            Grid.SetColumn(cellBorder, c);
                            tableGrid.Children.Add(cellBorder);
                        }
                    }

                    ChangelogPanel.Children.Add(tableGrid);
                }
            }
            else
            {
                var tb = CreateInlineTextBlock(line.Trim());
                tb.Margin = new Thickness(0, 2, 0, 2);
                ChangelogPanel.Children.Add(tb);
                i++;
            }
        }
    }

    private TextBlock CreateInlineTextBlock(string text)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Opacity = 0.88
        };

        var parts = SplitByInlineCode(text);
        foreach (var part in parts)
        {
            if (part.IsCode)
            {
                var codeBorder = new Border
                {
                    Background = (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Child = new TextBlock
                    {
                        Text = part.Text,
                        FontFamily = new FontFamily("Cascadia Code, Consolas"),
                        FontSize = 13
                    }
                };
                tb.Inlines.Add(new InlineUIContainer { Child = codeBorder });
            }
            else
            {
                tb.Inlines.Add(new Run { Text = part.Text });
            }
        }

        return tb;
    }

    private record InlinePart(string Text, bool IsCode);

    private static List<InlinePart> SplitByInlineCode(string text)
    {
        var parts = new List<InlinePart>();
        var span = text.AsSpan();
        var currentPos = 0;

        while (currentPos < span.Length)
        {
            var backtickPos = span[currentPos..].IndexOf('`');
            if (backtickPos < 0)
            {
                var remaining = span[currentPos..].ToString();
                if (remaining.Length > 0)
                    parts.Add(new InlinePart(remaining, false));
                break;
            }

            var codeStart = currentPos + backtickPos + 1;
            if (codeStart >= span.Length)
            {
                parts.Add(new InlinePart(span[currentPos..].ToString(), false));
                break;
            }

            if (backtickPos > 0)
                parts.Add(new InlinePart(span[currentPos..(currentPos + backtickPos)].ToString(), false));

            var endBacktick = span[codeStart..].IndexOf('`');
            if (endBacktick < 0)
            {
                parts.Add(new InlinePart(span[(currentPos + backtickPos)..].ToString(), false));
                break;
            }

            var codeText = span[codeStart..(codeStart + endBacktick)].ToString();
            parts.Add(new InlinePart(codeText, true));
            currentPos = codeStart + endBacktick + 1;
        }

        return parts;
    }

    private static string StripMarkdownFormatting(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.+?)\]\(.+?\)", "$1");
        return text.Trim();
    }
}
