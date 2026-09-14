using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class FirmwareInputTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData(" 1,2,3 ")]
    [InlineData("1.2,3")]
    public void Accepts_dots_and_commas(string typed) =>
        Assert.Equal(new Version(1, 2, 3), FirmwareInput.Parse(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1,,2")]
    public void Rejects_what_is_not_a_version(string typed) =>
        Assert.Null(FirmwareInput.Parse(typed));
}
