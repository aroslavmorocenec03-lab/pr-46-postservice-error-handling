using System.Diagnostics;
using System.Globalization;
using PostService.Client;

namespace PostService.WindowsApp;

internal static class Program
{
    private static int _reporting;

    [STAThread]
    private static void Main()
    {
        // Реєстрація до створення будь-якого вікна.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal(e.ExceptionObject as Exception ?? new Exception("Невідомий необроблений виняток."));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { ReportFatal(e.Exception); Application.Exit(); };

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
        ApplicationConfiguration.Initialize();
        string? configuredUrl = Environment.GetEnvironmentVariable("POST_SERVICE_URL");
        using var client = new PostServiceClient(configuredUrl is null ? null : new Uri(configuredUrl));
        Application.Run(new PostingList(client));
    }

    private static void ReportFatal(Exception exception)
    {
        if (Interlocked.Exchange(ref _reporting, 1) != 0) return;
        Environment.ExitCode = 1;
        string report = ExceptionDiagnostics.Format(exception);
        Debug.WriteLine(report);
        // Додатковий журнал доступний і при запуску без відладчика.
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PostService.WindowsApp");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), report);
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Не вдалося записати додатковий журнал: " + logError);
        }
        MessageBox.Show("Сталася непередбачена помилка. Застосунок буде закрито. Повідомте розробника та передайте журнал помилок.",
            "Непередбачена помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
