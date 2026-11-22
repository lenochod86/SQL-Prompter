using SqlPrompter.Core.Editing;
using Xunit;

namespace SqlPrompter.Tests
{
    public class PopupPlacementTests
    {
        private static (double Left, double Top) Place(double x, double y, double w, double h, double l, double t, double r, double b)
        {
            PopupPlacement.Place(x, y, 20, 4, w, h, l, t, r, b, out var left, out var top);
            return (left, top);
        }

        [Fact]
        public void OpensBelowTheCaretWhenThereIsRoom()
        {
            Assert.Equal((100d, 200d), Place(100, 200, 420, 340, 0, 0, 1920, 1040));
        }

        [Fact]
        public void MovesLeftAtTheRightEdge()
        {
            Assert.Equal((1500d, 200d), Place(1800, 200, 420, 340, 0, 0, 1920, 1040));
        }

        [Fact]
        public void OpensAboveTheLineAtTheBottomEdge()
        {
            Assert.Equal((100d, 900d - 340 - 20 - 4), Place(100, 900, 420, 340, 0, 0, 1920, 1040));
        }

        [Fact]
        public void MonitorWithNegativeCoordinatesLeftOfThePrimary()
        {
            Assert.Equal((-1500d, 300d), Place(-1500, 300, 420, 340, -1920, 0, 0, 1040));
        }

        [Fact]
        public void MovesLeftAtTheRightEdgeOfAMonitorWithNegativeCoordinates()
        {
            Assert.Equal((-420d, 300d), Place(-50, 300, 420, 340, -1920, 0, 0, 1040));
        }

        [Fact]
        public void MonitorAbovethePrimary()
        {
            Assert.Equal((200d, -900d), Place(200, -900, 420, 340, 0, -1080, 1920, 0 - 40));
        }

        [Fact]
        public void OpensAboveTheLineOnAMonitorAbovethePrimary()
        {
            // work area -1080..-40; the line is at -100, so below it there is no room
            Assert.Equal((200d, -100d - 340 - 20 - 4), Place(200, -100, 420, 340, 0, -1080, 1920, -40));
        }

        [Fact]
        public void MonitorBelowThePrimaryWithAnotherSize()
        {
            Assert.Equal((2000d, 1500d), Place(2000, 1500, 420, 340, 1280, 1080, 2560, 2000));
            Assert.Equal((2560d - 420, 1500d), Place(2550, 1500, 420, 340, 1280, 1080, 2560, 2000));
        }

        [Fact]
        public void PopupLargerThanTheAreaStaysAtTheTopLeft()
        {
            Assert.Equal((0d, 0d), Place(500, 300, 420, 340, 0, 0, 300, 200));
        }

        [Fact]
        public void CaretOnTheVeryEdge()
        {
            Assert.Equal((1500d, 700d), Place(1920, 700, 420, 340, 0, 0, 1920, 1040));
        }

        [Fact]
        public void CaretOnTheLeftEdgeOfTheArea()
        {
            Assert.Equal((-1920d, 10d), Place(-1930, 10, 420, 340, -1920, 0, 0, 1040));
        }

        private static (double Top, double Height) Vertical(double lineTop, double lineBottom, double height, double area = 1000, double min = 100)
        {
            PopupPlacement.PlaceVertical(lineTop, lineBottom, height, min, 2, 0, area, out var top, out var fit);
            return (top, fit);
        }

        [Fact]
        public void VerticalOpensBelowTheLineWhenItFits()
        {
            Assert.Equal((102d, 300d), Vertical(80, 100, 300));
        }

        [Fact]
        public void VerticalOpensAboveTheLineWhenOnlyThereIsRoom()
        {
            // The bottom edge ends 2 above the line, the whole line stays visible.
            Assert.Equal((900d - 2 - 300, 300d), Vertical(900, 920, 300));
        }

        [Fact]
        public void VerticalShrinksOnTheSideWithMoreRoom()
        {
            var (top, height) = Vertical(500, 520, 900);
            Assert.Equal((0d, 498d), (top, height));
        }

        [Fact]
        public void VerticalShrinksAboveWithoutCoveringTheLine()
        {
            var (top, height) = Vertical(700, 720, 900, area: 760);
            Assert.Equal(700d - 2, top + height);
            Assert.Equal(698d, height);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.93)]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void VerticalNeverCoversTheLineAtAnyZoom(double zoom)
        {
            var lineTop = 880 * zoom;
            var lineBottom = 900 * zoom;
            PopupPlacement.PlaceVertical(lineTop, lineBottom, 400, 100, 2, 0, 1000 * zoom, out var top, out var fit);
            Assert.True(top + fit <= lineTop || top >= lineBottom);
        }

        [Fact]
        public void VerticalKeepsTheMinimumHeightEvenWhenThereIsNoRoom()
        {
            var (top, height) = Vertical(40, 60, 400, area: 100, min: 100);
            Assert.Equal(100d, height);
            Assert.True(top >= 60);
        }
    }
}
