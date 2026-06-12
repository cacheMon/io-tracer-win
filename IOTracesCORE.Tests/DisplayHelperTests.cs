using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class DisplayHelperTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(42, "42")]
        [InlineData(999, "999")]
        public void ToPowerOfTen_SmallValues_NoSeparatorOrExponent(long input, string expected)
        {
            // Values below the thousands separator threshold render identically across cultures.
            Assert.Equal(expected, DisplayHelper.ToPowerOfTen(input));
        }

        [Fact]
        public void ToPowerOfTen_LargeValue_UsesScientificNotation()
        {
            // 1,000,000 => 1.xx × 10⁶ — assert the culture-independent exponent portion.
            string result = DisplayHelper.ToPowerOfTen(1_000_000);
            Assert.Contains("× 10⁶", result);
        }

        [Theory]
        [InlineData(0, "⁰")]
        [InlineData(6, "⁶")]
        [InlineData(123, "¹²³")]
        [InlineData(-5, "⁻⁵")]
        public void ToSuperscript_MapsDigitsAndSign(int input, string expected)
        {
            Assert.Equal(expected, DisplayHelper.ToSuperscript(input));
        }
    }
}
