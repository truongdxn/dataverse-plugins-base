using System;

namespace Dataverse.Plugins.VsCommands;

/// <summary>
/// The identifiers shared with DvCommandsPackage.vsct.
/// <para>
/// Every value here appears twice - once in the command table Visual Studio reads, once in the C#
/// that handles the click - and a mismatch produces no error at all: the menu item simply does
/// nothing. They are kept adjacent and named identically for that reason.
/// </para>
/// </summary>
internal static class PackageGuids
{
    public const string PackageString = "29d3e9c7-d1df-48ff-abd4-712cc61fcb61";

    public const string CommandSetString = "f9c58263-c38c-4ea6-afd2-9f0ac80884bf";

    public static readonly Guid CommandSet = new Guid(CommandSetString);

    /// <summary>Identifies the Output window pane, which is created once and reused.</summary>
    public static readonly Guid OutputPane = new Guid("f718f036-14ce-4423-8eee-c3e5533b7f34");
}

/// <summary>Command ids. Must match the IDSymbol values in the .vsct exactly.</summary>
internal static class PackageIds
{
    // Project node - everything narrows to the clicked assembly with -a.
    public const int BuildAssembly = 0x0100;
    public const int TestAssembly = 0x0101;
    public const int ValidateAssembly = 0x0102;
    public const int ManifestAssembly = 0x0103;
    public const int SyncAssembly = 0x0104;
    public const int NewPlugin = 0x0105;
    public const int NewTests = 0x0106;

    // Solution node - repo-wide, run from the repo root.
    public const int BuildSolution = 0x0200;
    public const int TestSolution = 0x0201;
    public const int ValidateSolution = 0x0202;
    public const int ManifestSolution = 0x0203;
    public const int PackSolution = 0x0204;
    public const int SyncSolution = 0x0205;
    public const int ListSolutions = 0x0206;
    public const int ListEnvironments = 0x0207;
    public const int SchemaPull = 0x0208;
    public const int SchemaCodegen = 0x0209;
    public const int MessagesPull = 0x020A;
    public const int NewSolution = 0x020B;
    public const int NewAssembly = 0x020C;
}
