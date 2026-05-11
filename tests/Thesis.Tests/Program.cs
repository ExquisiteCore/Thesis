internal static partial class Program
{
    private static void Main()
    {
        var failures = new List<string>();
        foreach (var (name, test) in Tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine(ex);
            }
        }

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{failures.Count} test(s) failed.");
            Environment.Exit(1);
        }
    }
}
