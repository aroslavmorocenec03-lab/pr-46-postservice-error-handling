using System.Reflection;

namespace PostService.Client;

public static class ExceptionDiagnostics
{
    public static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null })
            exception = exception.InnerException;
        return exception;
    }

    public static string Format(Exception exception)
    {
        Exception actual = Unwrap(exception);
        return $"[{DateTimeOffset.Now:O}]{Environment.NewLine}Тип: {actual.GetType().FullName}{Environment.NewLine}"
            + $"Повідомлення: {actual.Message}{Environment.NewLine}Повний виняток і стек:{Environment.NewLine}{exception}{Environment.NewLine}";
    }
}
