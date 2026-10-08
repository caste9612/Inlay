using System.Windows;
using System.Windows.Controls;
using Quiver.App.Views;

namespace Quiver.App.Tests;

public sealed class CoverPanelTests
{
    [Fact]
    public void TheFirstChildGivesTheSize_TheCardOverItNeverMakesItGrow() => StaThread.Run(() =>
    {
        var tile = new Border { Width = 150, Height = 60 };
        var card = new TextBlock
        {
            Text = "Un titolo molto lungo, che da lontano non entra nel riquadro",
            FontSize = 24,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 100),
        };
        var panel = new CoverPanel { Children = { tile, card } };

        panel.Measure(new Size(280, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 280, panel.DesiredSize.Height));

        Assert.Equal(new Size(150, 60), panel.DesiredSize);
        // The card is cut where the room ends, not where the first child would end.
        Assert.True(card.DesiredSize.Width > 150);
        Assert.Equal(new Size(280, 60), new Size(card.ActualWidth + card.Margin.Left + card.Margin.Right, panel.ActualHeight));
    });
}
