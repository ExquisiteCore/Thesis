namespace Thesis.Host;

public sealed class HostApplicationException : Exception
{
    public HostApplicationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public HostApplicationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
