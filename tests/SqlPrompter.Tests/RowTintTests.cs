using SqlPrompter.Core.Completion;
using Xunit;

namespace SqlPrompter.Tests
{
    public class RowTintTests
    {
        [Fact]
        public void Blend_MixesByRatio()
        {
            Assert.Equal(0x808080, RowTint.Blend(0xFFFFFF, 0x000000, 0.5));
            Assert.Equal(0x000000, RowTint.Blend(0xFFFFFF, 0x000000, 0));
            Assert.Equal(0xFFFFFF, RowTint.Blend(0xFFFFFF, 0x000000, 1));
            Assert.Equal(0x0000FF, RowTint.Blend(0x0000FF, 0xFF0000, 5));
        }

        [Fact]
        public void Contrast_BlackOnWhiteIs21()
        {
            Assert.Equal(21, RowTint.Contrast(0x000000, 0xFFFFFF), 3);
            Assert.Equal(1, RowTint.Contrast(0x123456, 0x123456), 3);
        }

        [Fact]
        public void Background_KeepsTheTextReadableInLightTheme()
        {
            var bg = RowTint.Background(0x0078D7, 0xFFFFFF, 0xFF00FF, RowTint.SelectedRatio);
            Assert.True(RowTint.Contrast(0xFF00FF, bg) >= RowTint.MinContrast);
            Assert.NotEqual(0xFFFFFF, bg);
        }

        [Fact]
        public void Background_KeepsTheTextReadableInDarkTheme()
        {
            var bg = RowTint.Background(0x0E639C, 0x1F1F1F, 0xDCDCDC, RowTint.SelectedRatio);
            Assert.True(RowTint.Contrast(0xDCDCDC, bg) >= RowTint.MinContrast);
        }

        [Fact]
        public void Background_WeakensTheTintForALowContrastText()
        {
            var strong = RowTint.Blend(0x0E639C, 0x1F1F1F, 0.6);
            var chosen = RowTint.Background(0x0E639C, 0x1F1F1F, 0x707070, 0.6);
            Assert.NotEqual(strong, chosen);
        }
    }
}
