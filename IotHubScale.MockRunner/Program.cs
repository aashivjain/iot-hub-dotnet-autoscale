using IotHubScale.Core;

var scenarios = new[]
{
    new
    {
        Name = "Below threshold stays at same unit",
        Sku = "S1",
        Units = 1L,
        Messages = 300000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = false,
        ExpectedUnits = 1L
    },
    new
    {
        Name = "At threshold adds one unit",
        Sku = "S1",
        Units = 1L,
        Messages = 360000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = true,
        ExpectedUnits = 2L
    },
    new
    {
        Name = "Above threshold adds one unit",
        Sku = "S1",
        Units = 1L,
        Messages = 380000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = true,
        ExpectedUnits = 2L
    },
    new
    {
        Name = "Higher S1 unit count adds one more unit",
        Sku = "S1",
        Units = 5L,
        Messages = 1800000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = true,
        ExpectedUnits = 6L
    },
    new
    {
        Name = "Higher S1 unit count below threshold does not scale",
        Sku = "S1",
        Units = 5L,
        Messages = 1700000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = false,
        ExpectedUnits = 5L
    },
    new
    {
        Name = "S2 threshold also adds one unit",
        Sku = "S2",
        Units = 2L,
        Messages = 10800000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = true,
        ExpectedUnits = 3L
    },
    new
    {
        Name = "S3 below threshold does not scale",
        Sku = "S3",
        Units = 1L,
        Messages = 250000000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = false,
        ExpectedUnits = 1L
    },
    new
    {
        Name = "S3 at threshold adds one unit",
        Sku = "S3",
        Units = 1L,
        Messages = 270000000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = true,
        ExpectedUnits = 2L
    },
    new
    {
        Name = "S1 max unit count does not scale further",
        Sku = "S1",
        Units = 200L,
        Messages = 80000000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = false,
        ExpectedUnits = 200L
    },
    new
    {
        Name = "Unknown SKU does not scale",
        Sku = "B1",
        Units = 1L,
        Messages = 100000L,
        ThresholdPercent = 90,
        ExpectedShouldScale = false,
        ExpectedUnits = 1L
    }
};

var failures = 0;

foreach (var scenario in scenarios)
{
    var decision = ScaleLogic.EvaluateScale(
        scenario.Sku,
        scenario.Units,
        scenario.Messages,
        scenario.ThresholdPercent);

    var passed = decision.ShouldScale == scenario.ExpectedShouldScale
        && decision.TargetUnits == scenario.ExpectedUnits
        && decision.TargetSku == scenario.Sku;

    Console.WriteLine($"{scenario.Name}: {(passed ? "PASS" : "FAIL")}");
    Console.WriteLine($"  Input: sku={scenario.Sku}, units={scenario.Units}, messages={scenario.Messages}, threshold={scenario.ThresholdPercent}%");
    Console.WriteLine($"  Threshold messages: {decision.MessageLimit}");
    Console.WriteLine($"  Result: shouldScale={decision.ShouldScale}, target={decision.TargetSku}-{decision.TargetUnits}");
    Console.WriteLine($"  Reason: {decision.Reason}");
    Console.WriteLine();

    if (!passed)
        failures++;
}

Environment.ExitCode = failures == 0 ? 0 : 1;
