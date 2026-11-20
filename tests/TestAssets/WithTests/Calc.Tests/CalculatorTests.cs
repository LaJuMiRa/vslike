namespace Calc.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_Works() => Assert.Equal(3, Calculator.Add(1, 2));

    [Fact]
    public void Add_Wrong() => Assert.Equal(4, Calculator.Add(1, 2));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Add_Zero(int x) => Assert.Equal(x, Calculator.Add(x, 0));
}
