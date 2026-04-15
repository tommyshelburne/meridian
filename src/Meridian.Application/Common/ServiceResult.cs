namespace Meridian.Application.Common;

public class ServiceResult<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess => Error is null;

    private ServiceResult(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public static ServiceResult<T> Ok(T value) => new(value, null);
    public static ServiceResult<T> Fail(string error) => new(default, error);

    public ServiceResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? ServiceResult<TOut>.Ok(map(Value!)) : ServiceResult<TOut>.Fail(Error!);
}

public class ServiceResult
{
    public string? Error { get; }
    public bool IsSuccess => Error is null;

    private ServiceResult(string? error) => Error = error;

    public static ServiceResult Ok() => new(null);
    public static ServiceResult Fail(string error) => new(error);
}
