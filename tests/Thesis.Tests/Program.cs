using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static partial class Program
{
    internal static IEnumerable<object[]> CaseNames => Tests.Select(t => new object[] { t.Name });
    internal static void RunCase(string name) => Tests.Single(t => t.Name == name).Test();
}

public sealed class RegressionTests
{
    public static IEnumerable<object[]> Cases => Program.CaseNames;

    [Theory]
    [MemberData(nameof(Cases))]
    public void Regression(string name) => Program.RunCase(name);
}
