using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class LoadShedderTests
    {
        [Theory]
        // HIGH is never shed, at any level.
        [InlineData(FsPriority.High, 0, false)]
        [InlineData(FsPriority.High, 1, false)]
        [InlineData(FsPriority.High, 2, false)]
        // Level 0: keep everything.
        [InlineData(FsPriority.Med, 0, false)]
        [InlineData(FsPriority.Low, 0, false)]
        // Level 1: shed LOW only.
        [InlineData(FsPriority.Low, 1, true)]
        [InlineData(FsPriority.Med, 1, false)]
        // Level 2: shed LOW + MED.
        [InlineData(FsPriority.Low, 2, true)]
        [InlineData(FsPriority.Med, 2, true)]
        public void ShedDecision_RespectsPriorityAndLevel(FsPriority pri, int level, bool expected)
        {
            Assert.Equal(expected, LoadShedder.ShedDecision(pri, level));
        }

        [Theory]
        // From 0: rise to 1 only above the enter-LOW threshold (3s).
        [InlineData(0, 0.0, 0)]
        [InlineData(0, 2.9, 0)]
        [InlineData(0, 3.1, 1)]
        // From 1: rise to 2 above 12s; fall to 0 below the recover-LOW threshold (1.5s); else hold.
        [InlineData(1, 5.0, 1)]
        [InlineData(1, 12.1, 2)]
        [InlineData(1, 1.4, 0)]
        [InlineData(1, 1.6, 1)]
        // From 2: fall to 1 below the recover-MED threshold (6s); else hold. Hysteresis means it
        // steps down to 1 (not straight to 0) even at very low lag.
        [InlineData(2, 20.0, 2)]
        [InlineData(2, 5.9, 1)]
        [InlineData(2, 6.1, 2)]
        [InlineData(2, 0.0, 1)]
        public void NextLevel_HysteresisBoundaries(int current, double lagSec, int expected)
        {
            Assert.Equal(expected, LoadShedder.NextLevel(current, lagSec));
        }
    }
}
