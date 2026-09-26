using System.Drawing;
using ClaudeMeter.UI;

namespace ClaudeMeter.Tests;

public class PanelPlacementTests
{
    private static readonly Size Panel = new(320, 240);

    [Fact]
    public void Sits_above_icon_on_bottom_taskbar_centered_on_it()
    {
        var workArea = new Rectangle(0, 0, 1920, 1032);
        var icon = new Rectangle(1700, 1040, 24, 32);

        Assert.Equal(new Point(1552, 784), PanelPlacement.Compute(icon, Panel, workArea, gap: 8));
    }

    [Fact]
    public void Is_clamped_inside_work_area_near_screen_edge()
    {
        var workArea = new Rectangle(0, 0, 1920, 1032);
        var icon = new Rectangle(1890, 1040, 24, 32);

        Assert.Equal(new Point(1592, 784), PanelPlacement.Compute(icon, Panel, workArea, gap: 8));
    }

    [Fact]
    public void Sits_below_icon_on_top_taskbar()
    {
        var workArea = new Rectangle(0, 48, 1920, 1032);
        var icon = new Rectangle(1700, 8, 24, 32);

        Assert.Equal(new Point(1552, 56), PanelPlacement.Compute(icon, Panel, workArea, gap: 8));
    }
}
