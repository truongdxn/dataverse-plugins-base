namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>Console output. Everything the tool says to a human goes through here.</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>Set by --verbose. Detail output is suppressed unless it is on.</summary>
    public static bool Verbose { get; set; }

    public static void Info(string message) => Write(message, null);

    public static void Success(string message) => Write(message, ConsoleColor.Green);

    /// <summary>
    /// Deliberately stdout, not stderr. Windows PowerShell 5.1 turns anything a native command
    /// writes to stderr into a NativeCommandError, so a warning on stderr renders as a failure
    /// and buries the real output. Errors still go to stderr.
    /// </summary>
    public static void Warn(string message) => Write("warning: " + message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("error: " + message, ConsoleColor.Red, error: true);

    public static void Detail(string message)
    {
        if (Verbose)
        {
            Write("  " + message, ConsoleColor.DarkGray);
        }
    }

    public static void Heading(string message)
    {
        Write(string.Empty, null);
        Write(message, ConsoleColor.Cyan);
    }

    /// <summary>Indented list item, for reporting what changed.</summary>
    public static void Item(string message) => Write("  " + message, null);

    private static void Write(string message, ConsoleColor? color, bool error = false)
    {
        lock (Gate)
        {
            var writer = error ? Console.Error : Console.Out;

            if (color.HasValue && !Console.IsOutputRedirected)
            {
                var previous = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
                writer.WriteLine(message);
                Console.ForegroundColor = previous;
            }
            else
            {
                writer.WriteLine(message);
            }
        }
    }
}
