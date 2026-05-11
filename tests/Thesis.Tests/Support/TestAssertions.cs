internal static partial class Program
{
    static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new UnreachableException($"Expected '{expected}', got '{actual}'.");
        }
    }

    static void AssertContains(string text, string expected)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new UnreachableException($"Expected text to contain '{expected}'.");
        }
    }

    static void AssertDoesNotContain(string text, string unexpected)
    {
        if (text.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new UnreachableException($"Expected text not to contain '{unexpected}'.");
        }
    }

    static void AssertBytesEqual(byte[] expected, byte[] actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new UnreachableException($"Expected bytes '{Convert.ToHexString(expected)}', got '{Convert.ToHexString(actual)}'.");
        }
    }
}
