using Quiver.Core;

namespace Quiver.Tests;

/// <summary>Where the zones of the canvas go: in order, as high as there is room, never one over another.</summary>
public sealed class CanvasPlacementTests
{
    private const double Gap = 40;

    [Fact]
    public void Zones_FillARow_ThenGoUnderTheShortestPart()
    {
        // Two zones fit in the row; the third goes under the shorter of the two, on the right.
        var places = CanvasPlacement.Place([new(1000, 600), new(1000, 300), new(1000, 200)], rowWidth: 2100, Gap);

        Assert.Equal(new CanvasRect(0, 0, 1000, 600), places[0]);
        Assert.Equal(new CanvasRect(1040, 0, 1000, 300), places[1]);
        Assert.Equal(new CanvasRect(1040, 340, 1000, 200), places[2]);
    }

    [Fact]
    public void AZoneWiderThanTheRow_StartsAtTheLeftEdge_UnderTheOthers()
    {
        var places = CanvasPlacement.Place([new(800, 400), new(3000, 100)], rowWidth: 2000, Gap);

        Assert.Equal(new CanvasRect(0, 440, 3000, 100), places[1]);
    }

    [Fact]
    public void ZonesPutByTheUser_StayWhereTheyAre_AndTheOthersGoAroundThem()
    {
        var places = CanvasPlacement.Place([new(500, 500), new(600, 300, X: 0, Y: 0), new(500, 200)], rowWidth: 2000, Gap);

        Assert.Equal(new CanvasRect(0, 0, 600, 300), places[1]);
        Assert.Equal(new CanvasRect(640, 0, 500, 500), places[0]);
        Assert.Equal(new CanvasRect(1180, 0, 500, 200), places[2]);
    }

    [Fact]
    public void TheRows_AreAsWideAsShowsTheZonesBiggest_OnAWideScreen()
    {
        // Three squares on a 16:9 screen look biggest side by side; a tall one beside short ones, next to them.
        Assert.Equal(3 * 1000 + 2 * Gap, CanvasPlacement.BestRowWidth([new(1000, 1000), new(1000, 1000), new(1000, 1000)], Gap, 16.0 / 9));

        ZoneBox[] zones = [new(610, 190), new(910, 830), new(610, 120), new(910, 150)];
        var width = CanvasPlacement.BestRowWidth(zones, Gap, 16.0 / 9);
        var places = CanvasPlacement.Place(zones, width, Gap);
        Assert.Equal(0, places[1].Y);
        Assert.True(places.Max(place => place.Bottom) < 1200, "the zones make a column");
    }

    [Fact]
    public void ManyZones_NeverCoverEachOther_AndStayInTheRow()
    {
        var random = new Random(7);
        var zones = Enumerable.Range(0, 40)
            .Select(_ => new ZoneBox(random.Next(300, 1300), random.Next(100, 900)))
            .ToList();

        var places = CanvasPlacement.Place(zones, rowWidth: 3000, Gap);

        for (var i = 0; i < places.Count; i++)
        {
            Assert.Equal(zones[i].Width, places[i].Width);
            Assert.True(places[i].Right <= 3000, $"zone {i} goes past the row");
            for (var j = i + 1; j < places.Count; j++)
                Assert.False(places[i].Overlaps(places[j]), $"zones {i} and {j} overlap");
        }
    }
}
