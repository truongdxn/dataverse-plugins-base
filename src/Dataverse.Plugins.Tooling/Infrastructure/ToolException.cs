namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>
/// A failure that is the user's to fix (bad config, missing file, invalid step). Program.cs
/// prints just the message for these, with no stack trace - a stack trace here is noise that
/// hides the actionable part.
/// </summary>
public sealed class ToolException : Exception
{
    public ToolException(string message) : base(message)
    {
    }

    public ToolException(string message, Exception inner) : base(message, inner)
    {
    }
}
