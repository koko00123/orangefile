using Orange.Core;

namespace Orange.Tests;

public class SmokeTests
{
    // Proves the test project can load the app assembly; real tests start with the Desktop module.
    [Fact]
    public void AppAssemblyLoads()
    {
        Assert.Equal("Orange", typeof(Settings).Assembly.GetName().Name);
    }
}
