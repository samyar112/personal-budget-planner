using backend.DTOs;
using backend.Services;

namespace backend.Tests;

/*
 * Test cases:
 * 1. Spending above income puts NET_NEGATIVE first.
 * 2. A typical month yields at least three candidates, and every template passes its own number check.
 * 3. An empty payload yields no candidates and a "no complete month" data gap.
 * 4. The number check accepts exact and rounded values and rejects an invented one.
 * 5. A reply naming an unknown candidate, repeating one, or using prohibited wording loses that tip.
 * 6. A reply that is not JSON yields no AI tips; too few valid ones are topped up with templates.
 * 7. A reply wrapped in a markdown code fence is still read.
 * 8. The cut tip targets the largest flexible category, never rent, even when rent is larger.
 * 9. The savings tip steps up five points instead of jumping to 20%.
 * 10. The buffer tip is half the monthly surplus with a time to one month of spending, and is absent with no surplus.
 * 11. Spending above income says how much of the gap a 15% cut in the largest flexible category covers.
 * 12. With no income found there is no gap or savings tip, the flexible cut is offered, and a data gap says why.
 * 13. Combine keeps the AI tips and their order, marks each tip's source, and fills to three from templates.
 * 14. The buffer roadmap saves half the surplus toward 1, 3 and 6 months of spending, each with a month.
 * 15. Without a surplus, the gap tip's roadmap starts with the flexible cut, then builds the buffer from what it
 *     frees; milestones more than ten years out are left off.
 * 16. The savings roadmap climbs five points at a time to 20%, with the extra each month at each step.
 */
public sealed class AiTipsRulesTests
{
    private static AiPayload Payload(
        decimal? income,
        decimal? spending,
        decimal? savingsRate,
        IReadOnlyList<PayloadCategory>? categories = null,
        IReadOnlyList<PayloadRecurring>? recurring = null,
        int months = 3) =>
        new(
            "1.0",
            new DateOnly(2026, 11, 26),
            new PayloadCoverage(["checking"], months),
            [
                new PayloadMetric(AiPayloadBuilder.IncomeMonthlyMean, income, AiPayloadBuilder.Usd),
                new PayloadMetric(AiPayloadBuilder.SpendingMonthlyMean, spending, AiPayloadBuilder.Usd),
                new PayloadMetric(AiPayloadBuilder.NetCashFlowMean, income - spending, AiPayloadBuilder.Usd),
                new PayloadMetric(AiPayloadBuilder.SavingsRate, savingsRate, AiPayloadBuilder.Ratio),
            ],
            categories ?? [],
            recurring ?? []);

    private static AiPayload Typical() => Payload(
        3000m,
        2700m,
        0.1m,
        [new PayloadCategory("RENT_AND_UTILITIES", 1200m), new PayloadCategory("FOOD_AND_DRINK", 412.35m)],
        [
            new PayloadRecurring("ENTERTAINMENT", "monthly", 15.49m, true),
            new PayloadRecurring("RENT_AND_UTILITIES", "monthly", 1200m, false),
        ]);

    [Fact]
    public void Candidates_SpendingAboveIncome_RanksNetNegativeFirst()
    {
        var candidates = AiTips.Candidates(Payload(2000m, 2500m, -0.25m, [new PayloadCategory("TRAVEL", 900m)]));

        Assert.Equal(AiTips.NetNegative, candidates[0].Id);
        Assert.Contains("$500.00", candidates[0].Text);
        Assert.DoesNotContain(candidates, candidate => candidate.Id == AiTips.SavingsRate);
    }

    [Fact]
    public void Candidates_TypicalMonth_AreEnough_AndPassTheirOwnCheck()
    {
        var candidates = AiTips.Candidates(Typical());

        Assert.True(candidates.Count >= AiTips.MinTips);
        Assert.All(candidates, candidate =>
            Assert.True(AiTips.NumbersAllowed(candidate.Title + " " + candidate.Text, candidate.Numbers), candidate.Text));
    }

    [Fact]
    public void Candidates_EmptyPayload_AreNone_WithDataGap()
    {
        var payload = Payload(null, null, null, months: 0);

        Assert.Empty(AiTips.Candidates(payload));
        Assert.Contains(AiTips.DataGaps(payload), gap => gap.Contains("no complete month"));
    }

    [Theory]
    [InlineData("You spend about $412.35 on food.", true)]
    [InlineData("You spend about $412 on food.", true)]
    [InlineData("You spend about $1,200 on rent.", true)]
    [InlineData("Cut 10% to save money.", true)]
    [InlineData("You spend about $500 on food.", false)]
    [InlineData("Try the 50/30/20 rule.", false)]
    public void NumbersAllowed_AcceptsKnownAndRounded_RejectsInvented(string text, bool expected)
    {
        Assert.Equal(expected, AiTips.NumbersAllowed(text, [412.35m, 1200m, 10m]));
    }

    [Fact]
    public void Validate_DropsUnknownRepeatedAndProhibitedTips()
    {
        var candidates = AiTips.Candidates(Typical());
        var first = candidates[0];
        var reply = Reply(
            (first.Id, first.Title, first.Text),
            (first.Id, first.Title, first.Text),
            ("MADE_UP", "Free money", "Nothing here."),
            (candidates[1].Id, candidates[1].Title, "Invest in index funds instead."),
            (candidates[2].Id, candidates[2].Title, candidates[2].Text),
            (candidates[3].Id, candidates[3].Title, candidates[3].Text));

        var tips = AiTips.Validate(reply, candidates);

        Assert.NotNull(tips);
        Assert.Equal([first.Id, candidates[2].Id, candidates[3].Id], tips.Select(tip => tip.Id));
    }

    [Fact]
    public void Validate_NotJson_YieldsNoTips_AndTooFewAreToppedUp()
    {
        var candidates = AiTips.Candidates(Typical());

        Assert.Empty(AiTips.Validate("Here are your tips: save more.", candidates));
        var one = AiTips.Validate(Reply((candidates[0].Id, candidates[0].Title, candidates[0].Text)), candidates);
        Assert.Single(one);
        Assert.Equal(3, AiTips.Combine(one, candidates).Count);
    }

    [Fact]
    public void Candidates_NoIncome_SkipGapAndSavings_OfferFlexibleCut_AndSayWhy()
    {
        var payload = Payload(0m, 3500m, null,
            [new PayloadCategory("RENT_AND_UTILITIES", 1500m), new PayloadCategory("FOOD_AND_DRINK", 391.55m)]);

        var ids = AiTips.Candidates(payload).Select(candidate => candidate.Id).ToList();

        Assert.DoesNotContain(AiTips.NetNegative, ids);
        Assert.DoesNotContain(AiTips.SavingsRate, ids);
        Assert.Contains(AiTips.FlexibleCategory, ids);
        Assert.Contains(AiTips.DataGaps(payload), gap => gap.StartsWith("No income was found"));
    }

    [Fact]
    public void Combine_KeepsAiOrder_MarksSource_AndFillsFromTemplates()
    {
        var candidates = AiTips.Candidates(Typical());
        var ai = new List<AiTip> { new(candidates[2].Id, "Reworded", candidates[2].Text, AiTips.SourceAi, []) };

        var tips = AiTips.Combine(ai, candidates);

        Assert.Equal([candidates[2].Id, candidates[0].Id, candidates[1].Id], tips.Select(tip => tip.Id));
        Assert.Equal([AiTips.SourceAi, AiTips.SourceTemplate, AiTips.SourceTemplate], tips.Select(tip => tip.Source));
    }

    [Fact]
    public void Validate_ReadsReplyInsideCodeFence()
    {
        var candidates = AiTips.Candidates(Typical());
        var reply = "```json\n" + Reply(candidates.Take(3).Select(c => (c.Id, c.Title, c.Text)).ToArray()) + "\n```";

        Assert.Equal(3, AiTips.Validate(reply, candidates)?.Count);
    }

    [Fact]
    public void Candidates_CutTargetsFlexibleSpending_NotRent()
    {
        var cut = AiTips.Candidates(Typical()).Single(candidate => candidate.Id == AiTips.FlexibleCategory);

        Assert.Contains("food and drink", cut.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rent", cut.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$41.24", cut.Text);
    }

    [Fact]
    public void Candidates_SavingsSteppedUpFivePoints()
    {
        var savings = AiTips.Candidates(Payload(3000m, 2940m, 0.02m)).Single(candidate => candidate.Id == AiTips.SavingsRate);

        Assert.Contains("7.0%", savings.Text);
        Assert.Contains("$150.00", savings.Text);
        Assert.DoesNotContain("20%", savings.Text);
    }

    [Fact]
    public void Candidates_BufferIsHalfTheSurplus_AndAbsentWithoutOne()
    {
        var buffer = AiTips.Candidates(Typical()).Single(candidate => candidate.Id == AiTips.EmergencyBuffer);

        Assert.Contains("$150.00", buffer.Text);
        Assert.Contains("18 months", buffer.Text);
        Assert.DoesNotContain(AiTips.Candidates(Payload(2000m, 2500m, -0.25m)),
            candidate => candidate.Id == AiTips.EmergencyBuffer);
    }

    [Fact]
    public void Candidates_SpendingAboveIncome_ShowsShareOfGapAFlexibleCutCovers()
    {
        var payload = Payload(2000m, 2500m, -0.25m,
            [new PayloadCategory("RENT_AND_UTILITIES", 1500m), new PayloadCategory("FOOD_AND_DRINK", 600m)]);

        var gap = AiTips.Candidates(payload)[0];

        Assert.Equal(AiTips.NetNegative, gap.Id);
        Assert.Contains("food and drink", gap.Text);
        Assert.Contains("$90.00", gap.Text);
        Assert.Contains("18%", gap.Text);
        Assert.DoesNotContain(AiTips.Candidates(payload), candidate => candidate.Id == AiTips.FlexibleCategory);
    }

    [Fact]
    public void Roadmap_Buffer_HalfTheSurplus_ToOneThreeAndSixMonths()
    {
        var buffer = AiTips.Candidates(Typical()).Single(candidate => candidate.Id == AiTips.EmergencyBuffer);

        Assert.Equal(
            [
                new RoadmapStep("Save each month", 150m, "month", null),
                new RoadmapStep("1 month of spending", 2700m, "total", new DateOnly(2028, 5, 1)),
                new RoadmapStep("3 months of spending", 8100m, "total", new DateOnly(2031, 5, 1)),
                new RoadmapStep("6 months of spending", 16200m, "total", new DateOnly(2035, 11, 1)),
            ],
            buffer.Steps);
    }

    [Fact]
    public void Roadmap_NoSurplus_StartsWithTheFlexibleCut()
    {
        var payload = Payload(2000m, 2100m, -0.05m, [new PayloadCategory("FOOD_AND_DRINK", 900m)]);

        var gap = AiTips.Candidates(payload).Single(candidate => candidate.Id == AiTips.NetNegative);

        Assert.Equal(new RoadmapStep("Spend 15% less on food and drink", 135m, "month", null), gap.Steps[0]);
        Assert.Equal(new RoadmapStep("Save each month", 17.5m, "month", null), gap.Steps[1]);
        // 2100 / 17.50 is 120 months; 3 and 6 months of spending are over ten years away, so they are left off.
        Assert.Equal(new RoadmapStep("1 month of spending", 2100m, "total", new DateOnly(2036, 11, 1)), gap.Steps[2]);
        Assert.Equal(3, gap.Steps.Count);
    }

    [Fact]
    public void Roadmap_Savings_ClimbsFivePointsToTwenty()
    {
        var savings = AiTips.Candidates(Payload(3000m, 2940m, 0.02m)).Single(candidate => candidate.Id == AiTips.SavingsRate);

        Assert.Equal(
            [
                new RoadmapStep("Save 7.0% of income", 150m, "month", null),
                new RoadmapStep("Save 12.0% of income", 300m, "month", null),
                new RoadmapStep("Save 17.0% of income", 450m, "month", null),
                new RoadmapStep("Save 20.0% of income", 540m, "month", null),
            ],
            savings.Steps);
    }

    private static string Reply(params (string Id, string Title, string Text)[] tips) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            tips = tips.Select(tip => new { candidateId = tip.Id, title = tip.Title, text = tip.Text }),
        });
}
