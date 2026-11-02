using Rev.Core.Text;

namespace Rev.Core.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(0, "0 cards")]
    [InlineData(1, "1 card")]
    [InlineData(2, "2 cards")]
    public void CountsUseTheRightWord(int count, string expected) =>
        Assert.Equal(expected, count.Counted("card"));

    [Fact]
    public void IrregularPluralsCanBeGiven() =>
        Assert.Equal("2 mice", 2.Counted("mouse", "mice"));
}
