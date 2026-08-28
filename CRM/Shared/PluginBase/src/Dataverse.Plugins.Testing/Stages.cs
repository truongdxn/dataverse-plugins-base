namespace Dataverse.Plugins.Testing
{
    /// <summary>
    /// Pipeline stage numbers, so a test reads as PostOperation rather than 40.
    /// <para>
    /// Duplicated from the abstractions' Stage enum on purpose: the harness cannot reference that
    /// assembly (see <see cref="PluginTestHost"/>). The values are Dataverse's own and cannot
    /// change, and the tooling has a test pinning the enum to these same numbers.
    /// </para>
    /// </summary>
    public static class Stages
    {
        /// <summary>Before the transaction opens. Best place to reject input.</summary>
        public const int PreValidation = 10;

        /// <summary>Inside the transaction, before the write.</summary>
        public const int PreOperation = 20;

        /// <summary>Inside the transaction, after the write.</summary>
        public const int PostOperation = 40;
    }
}
