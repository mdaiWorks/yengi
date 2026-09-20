using System.Collections.Generic;
using Xunit;

namespace mdaiAgent.Tests;

public class PlanModeHelperTests
{
    [Fact]
    public void ParsePlanOptions_ParsesNumberedPlanStepsAndDescriptions()
    {
        var planText = @"1. Add authentication
- Implement JWT-based auth
- Validate tokens in middleware

2. Add authorization
- Create role-based access control
- Restrict admin routes";

        var options = PlanModeHelper.ParsePlanOptions(planText);

        Assert.Equal(2, options.Count);
        Assert.Equal("1", options[0].Id);
        Assert.Equal("Add authentication", options[0].Title);
        Assert.Contains("Implement JWT-based auth", options[0].Description);
        Assert.Contains("Validate tokens in middleware", options[0].Description);

        Assert.Equal("2", options[1].Id);
        Assert.Equal("Add authorization", options[1].Title);
        Assert.Contains("Create role-based access control", options[1].Description);
        Assert.Contains("Restrict admin routes", options[1].Description);
    }

    [Fact]
    public void ParsePlanOptions_ParsesUnnumberedLinesIntoSingleOption()
    {
        var planText = "Implement file backup and recovery for the project. Use a simple JSON manifest.";

        var options = PlanModeHelper.ParsePlanOptions(planText);

        Assert.Single(options);
        Assert.Equal("1", options[0].Id);
        Assert.Equal("Implement file backup and recovery for the project. Use a simple JSON manifest.", options[0].Title);
        Assert.Empty(options[0].Description);
    }
}
