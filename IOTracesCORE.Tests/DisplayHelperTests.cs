using System.Globalization;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class DisplayHelperTests
    {
        [Theory]
        [InlineData(6, "⁶")]
        [InlineData(0, "⁰")]
        [InlineData(123, "¹²³")]
        [InlineData(-5, "⁻⁵")]
        public void ToSuperscript_ConvertsDigits(int number, string expected)
        {
            Assert.Equal(expected, DisplayHelper.ToSuperscript(number));
        }

        [Theory]
        [InlineData(0L, "0")]
        [InlineData(999L, "999")]
        [InlineData(1000L, "1,000")]
        [InlineData(999_999L, "999,999")]
        public void ToPowerOfTen_BelowOneMillion_UsesGroupedDecimal(long count, string expected)
        {
            using var _ = new CultureScope(CultureInfo.InvariantCulture);
            Assert.Equal(expected, DisplayHelper.ToPowerOfTen(count));
        }

        [Fact]
        public void ToPowerOfTen_TwoMillion_UsesScientificNotation()
        {
            using var _ = new CultureScope(CultureInfo.InvariantCulture);
            Assert.Equal("2.00 × 10⁶", DisplayHelper.ToPowerOfTen(2_000_000));
        }

        [Fact]
        public void ToPowerOfTen_Onepoint5Billion_UsesScientificNotation()
        {
            using var _ = new CultureScope(CultureInfo.InvariantCulture);
            Assert.Equal("1.50 × 10⁹", DisplayHelper.ToPowerOfTen(1_500_000_000));
        }

        [Theory]
        [InlineData(-1000L, "-1,000")]
        [InlineData(-2_000_000L, "-2.00 × 10⁶")]
        public void ToPowerOfTen_NegativeNumbers_FormatsCorrectly(long count, string expected)
        {
            using var _ = new CultureScope(CultureInfo.InvariantCulture);
            Assert.Equal(expected, DisplayHelper.ToPowerOfTen(count));
        }

        /// <summary>
        /// Temporarily overrides the current thread culture so that culture-sensitive
        /// number formatting (group separators, decimal points) is deterministic.
        /// </summary>
        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _previous;

            public CultureScope(CultureInfo culture)
            {
                _previous = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = culture;
            }

            public void Dispose() => CultureInfo.CurrentCulture = _previous;
        }
    }
}
