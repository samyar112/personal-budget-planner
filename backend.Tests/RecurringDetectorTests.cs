using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Merchant names are normalised: case, store numbers and punctuation ignored, first three words kept.
 * 2. Three monthly charges of the same amount are one monthly stream, with next date one month on.
 * 3. Weekly, every-two-weeks, quarterly and yearly cadences are recognised.
 * 4. Store numbers in the description do not split a stream.
 * 5. Fewer than three charges is not a stream.
 * 6. Amounts that vary more than 10% (and more than $2) are not a stream, e.g. groceries.
 * 7. A small amount drift within $2 or 10% still counts, and the typical amount is the median.
 * 8. Irregular intervals are not a stream.
 * 9. One late charge among several on-time ones is still a stream.
 * 10. A stream whose next charge is overdue by more than the tolerance is inactive and left out.
 * 11. Monthly cost scales by cadence: weekly x52/12, yearly /12.
 * 12. Subscriptions are monthly or yearly, under $100, in entertainment or services; rent and quarterly are not.
 * 13. Streams are sorted by next date; merchant and category come from the latest charge.
 */
public sealed class RecurringDetectorTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    [Theory]
    [InlineData("NETFLIX.COM 8821", "NETFLIX COM")]
    [InlineData("netflix.com #9932", "NETFLIX COM")]
    [InlineData("SHELL OIL 5731 WICHITA KS", "SHELL OIL WICHITA")]
    [InlineData("SQ *BLUE BOTTLE COFFEE", "SQ BLUE BOTTLE")]
    public void NormalizeMerchant_IgnoresCaseNumbersAndPunctuation(string description, string expected)
    {
        Assert.Equal(expected, RecurringDetector.NormalizeMerchant(description));
    }

    [Fact]
    public void ThreeMonthlyCharges_AreAMonthlyStream()
    {
        var stream = Assert.Single(RecurringDetector.Detect(
            [Row("2026-09-10", "NETFLIX", 15.49m), Row("2026-10-10", "NETFLIX", 15.49m), Row("2026-11-10", "NETFLIX", 15.49m)],
            Today));

        Assert.Equal(("monthly", 15.49m, 3), (stream.Cadence, stream.TypicalAmount, stream.Occurrences));
        Assert.Equal((new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 10)), (stream.LastDate, stream.NextDate));
    }

    [Theory]
    [InlineData(7, "weekly")]
    [InlineData(14, "biweekly")]
    [InlineData(91, "quarterly")]
    [InlineData(365, "yearly")]
    public void OtherCadences_AreRecognised(int days, string cadence)
    {
        var last = Today.AddDays(-2);
        RecurringCandidate[] rows = [Row(last.AddDays(-2 * days), "GYM", 40m), Row(last.AddDays(-days), "GYM", 40m), Row(last, "GYM", 40m)];

        Assert.Equal(cadence, Assert.Single(RecurringDetector.Detect(rows, Today)).Cadence);
    }

    [Fact]
    public void StoreNumbers_DoNotSplitAStream()
    {
        var stream = Assert.Single(RecurringDetector.Detect(
            [Row("2026-09-03", "SPOTIFY 1123", 11.99m), Row("2026-10-03", "SPOTIFY 2231", 11.99m), Row("2026-11-03", "Spotify #88", 11.99m)],
            Today));

        Assert.Equal(3, stream.Occurrences);
    }

    [Fact]
    public void FewerThanThreeCharges_IsNotAStream()
    {
        Assert.Empty(RecurringDetector.Detect([Row("2026-10-10", "NETFLIX", 15.49m), Row("2026-11-10", "NETFLIX", 15.49m)], Today));
    }

    [Fact]
    public void VaryingAmounts_AreNotAStream()
    {
        Assert.Empty(RecurringDetector.Detect(
            [Row("2026-09-05", "KROGER", 82.10m), Row("2026-10-05", "KROGER", 140.55m), Row("2026-11-05", "KROGER", 61.30m)],
            Today));
    }

    [Fact]
    public void SmallDrift_StillCounts_TypicalIsMedian()
    {
        var stream = Assert.Single(RecurringDetector.Detect(
            [Row("2026-09-15", "CITY WATER", 41.00m), Row("2026-10-15", "CITY WATER", 43.50m), Row("2026-11-15", "CITY WATER", 42.20m)],
            Today));

        Assert.Equal((42.20m, 42.20m), (stream.TypicalAmount, stream.LastAmount));
    }

    [Fact]
    public void IrregularIntervals_AreNotAStream()
    {
        Assert.Empty(RecurringDetector.Detect(
            [Row("2026-08-01", "CAFE", 5m), Row("2026-08-04", "CAFE", 5m), Row("2026-09-20", "CAFE", 5m), Row("2026-11-20", "CAFE", 5m)],
            Today));
    }

    [Fact]
    public void OneLateCharge_AmongOnTimeOnes_IsStillAStream()
    {
        RecurringCandidate[] rows =
        [
            Row("2026-06-01", "INTERNET CO", 60m), Row("2026-07-01", "INTERNET CO", 60m), Row("2026-08-01", "INTERNET CO", 60m),
            Row("2026-09-12", "INTERNET CO", 60m), Row("2026-10-01", "INTERNET CO", 60m), Row("2026-11-01", "INTERNET CO", 60m),
        ];

        Assert.Equal("monthly", Assert.Single(RecurringDetector.Detect(rows, Today)).Cadence);
    }

    [Fact]
    public void OverdueStream_IsInactive()
    {
        Assert.Empty(RecurringDetector.Detect(
            [Row("2026-05-10", "OLD GYM", 30m), Row("2026-06-10", "OLD GYM", 30m), Row("2026-07-10", "OLD GYM", 30m)],
            Today));
    }

    [Fact]
    public void MonthlyCost_ScalesByCadence()
    {
        RecurringCandidate[] rows =
        [
            Row("2026-11-05", "LAWN CARE", 30m), Row("2026-11-12", "LAWN CARE", 30m), Row("2026-11-19", "LAWN CARE", 30m),
            Row("2024-12-01", "DOMAIN HOST", 120m), Row("2025-12-01", "DOMAIN HOST", 120m), Row("2026-11-30", "DOMAIN HOST", 120m),
        ];

        var streams = RecurringDetector.Detect(rows, Today);

        Assert.Equal(130m, streams.Single(stream => stream.Cadence == "weekly").MonthlyCost);
        Assert.Equal(10m, streams.Single(stream => stream.Cadence == "yearly").MonthlyCost);
    }

    [Fact]
    public void Subscriptions_AreSmallEntertainmentOrServiceCharges()
    {
        RecurringCandidate[] rows =
        [
            Row("2026-09-10", "NETFLIX", 15.49m, "ENTERTAINMENT"), Row("2026-10-10", "NETFLIX", 15.49m, "ENTERTAINMENT"),
            Row("2026-11-10", "NETFLIX", 15.49m, "ENTERTAINMENT"),
            Row("2026-09-01", "LANDLORD", 1200m, "RENT_AND_UTILITIES"), Row("2026-10-01", "LANDLORD", 1200m, "RENT_AND_UTILITIES"),
            Row("2026-11-01", "LANDLORD", 1200m, "RENT_AND_UTILITIES"),
            Row("2026-05-15", "STREAMBOX", 20m, "ENTERTAINMENT"), Row("2026-08-15", "STREAMBOX", 20m, "ENTERTAINMENT"),
            Row("2026-11-15", "STREAMBOX", 20m, "ENTERTAINMENT"),
        ];

        var streams = RecurringDetector.Detect(rows, Today);

        Assert.True(streams.Single(stream => stream.Merchant == "NETFLIX").IsSubscription);
        Assert.False(streams.Single(stream => stream.Merchant == "STREAMBOX").IsSubscription);
        Assert.False(streams.Single(stream => stream.Merchant == "LANDLORD").IsSubscription);
    }

    [Fact]
    public void Streams_SortedByNextDate_LatestChargeNamesThem()
    {
        RecurringCandidate[] rows =
        [
            Row("2026-09-20", "SPOTIFY 1", 11.99m, "OTHER"), Row("2026-10-20", "SPOTIFY 2", 11.99m, "OTHER"),
            Row("2026-11-20", "Spotify", 11.99m, "ENTERTAINMENT"),
            Row("2026-09-05", "PHONE CO", 50m), Row("2026-10-05", "PHONE CO", 50m), Row("2026-11-05", "PHONE CO", 50m),
        ];

        var streams = RecurringDetector.Detect(rows, Today);

        Assert.Equal(["PHONE CO", "Spotify"], streams.Select(stream => stream.Merchant));
        Assert.Equal("ENTERTAINMENT", streams[1].Category);
    }

    private static RecurringCandidate Row(string date, string description, decimal amount, string? category = null) =>
        Row(DateOnly.Parse(date), description, amount, category);

    private static RecurringCandidate Row(DateOnly date, string description, decimal amount, string? category = null) =>
        new(date, description, amount, category);
}
