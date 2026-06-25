using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class TruncationStateTests
    {
        // ShouldArm fires on EITHER kernel OVERFLOW (authoritativeLost >= LossThreshold) OR a
        // consumer FREEZE (shed level >= 2 AND lag >= FreezeLagMs). See issue-#68 freeze pathology.
        [Theory]
        // Overflow arm: at/above the loss threshold, regardless of shed/lag.
        [InlineData(TruncationState.LossThreshold, 0, 0L, true)]
        [InlineData(TruncationState.LossThreshold + 1, 0, 0L, true)]
        [InlineData(50_000_000L, 0, 0L, true)]
        // Below threshold and no freeze => do not arm.
        [InlineData(TruncationState.LossThreshold - 1, 0, 0L, false)]
        [InlineData(0, 0, 0L, false)]
        // Freeze arm: top shed level (2) AND lag at/above FreezeLagMs, even with sub-threshold loss.
        [InlineData(0, 2, TruncationState.FreezeLagMs, true)]
        [InlineData(500_000L, 2, TruncationState.FreezeLagMs, true)]
        [InlineData(0, 2, TruncationState.FreezeLagMs + 5_000, true)]
        // Freeze NOT armed: lag just below the freeze threshold.
        [InlineData(0, 2, TruncationState.FreezeLagMs - 1, false)]
        // Freeze NOT armed: high lag but not at the top shed level (a transient, not a freeze).
        [InlineData(0, 1, 600_000L, false)]
        [InlineData(0, 0, 600_000L, false)]
        // Freeze NOT armed: top shed level but no real lag (e.g. just after an idle blip).
        [InlineData(0, 2, 0L, false)]
        public void ShouldArm_overflow_or_freeze(long authoritativeLost, int shedLevel, long lagMs, bool expected)
        {
            Assert.Equal(expected, TruncationState.ShouldArm(authoritativeLost, shedLevel, lagMs));
        }
    }
}
