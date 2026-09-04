using System;
using System.Collections.Generic;

namespace Dataverse.Plugins.VsCommands.Core;

/// <summary>
/// Turns a menu click into dv arguments.
/// <para>
/// This is the whole translation layer, kept apart from Visual Studio so it can be tested on a
/// machine with no Visual Studio on it. The menu handlers gather values and call in here; nothing
/// else composes a dv command line.
/// </para>
/// <para>
/// <c>-s</c> is never passed. dv infers the solution from the working directory, and the extension
/// always runs in the clicked project's folder - so the two agree by construction, and a stale
/// selection cannot send a build at the wrong solution.
/// </para>
/// </summary>
public static class DvArguments
{
    public static IReadOnlyList<string> Build(string assembly, string configuration) =>
        Assembly("build", assembly, configuration);

    public static IReadOnlyList<string> Test(string assembly, string configuration) =>
        Assembly("test", assembly, configuration);

    public static IReadOnlyList<string> Validate(string assembly) =>
        Assembly("validate", assembly, null);

    public static IReadOnlyList<string> Manifest(string assembly, string configuration) =>
        Assembly("manifest", assembly, configuration);

    /// <summary>
    /// Registers into a sandbox. <paramref name="prune"/> DELETES registrations that exist there
    /// but are not declared in source, which is why the caller has to say so explicitly and why
    /// the CLI gives it no short form.
    /// </summary>
    public static IReadOnlyList<string> Sync(string assembly, string environment, bool prune, string configuration)
    {
        var arguments = Assembly("sync", assembly, configuration);
        var list = new List<string>(arguments);

        list.Add("-e");
        list.Add(Required(environment, "environment"));

        if (prune)
        {
            list.Add("--prune");
        }

        return list;
    }

    /// <summary>
    /// Builds the solution .zip. The version is the package's, not the tool's - which is why the
    /// CLI takes it as a value here and as a flag nowhere else.
    /// </summary>
    public static IReadOnlyList<string> Pack(string version, string configuration)
    {
        var list = new List<string> { "pack" };
        Add(list, "--version", version);
        Add(list, "-c", configuration);
        return list;
    }

    public static IReadOnlyList<string> Solutions() => new List<string> { "solutions" };

    public static IReadOnlyList<string> Environments() => new List<string> { "environments" };

    public static IReadOnlyList<string> SchemaPull(string environment, string tables)
    {
        var list = new List<string> { "schema", "pull", "-e", Required(environment, "environment") };
        Add(list, "-t", tables);
        return list;
    }

    public static IReadOnlyList<string> SchemaCodegen() => new List<string> { "schema", "codegen" };

    public static IReadOnlyList<string> MessagesPull(string environment) =>
        new List<string> { "messages", "pull", "-e", Required(environment, "environment") };

    /// <summary>
    /// Creates a solution, or adopts a folder Visual Studio already made - which is the case this
    /// exists for, since the New Project dialog cannot write solution.json.
    /// </summary>
    public static IReadOnlyList<string> NewSolution(string name, string prefix, string uniqueName)
    {
        var list = new List<string> { "new", "solution", Required(name, "name") };
        Add(list, "--prefix", prefix);
        Add(list, "--unique-name", uniqueName);
        return list;
    }

    public static IReadOnlyList<string> NewAssembly(string name) =>
        new List<string> { "new", "assembly", Required(name, "name") };

    public static IReadOnlyList<string> NewTests(string name, string forAssembly)
    {
        var list = new List<string> { "new", "tests", Required(name, "name") };
        Add(list, "--for", forAssembly);
        return list;
    }

    public static IReadOnlyList<string> NewPlugin(string name, string entity, string message, string stage)
    {
        var list = new List<string> { "new", "plugin", Required(name, "name") };
        Add(list, "--entity", entity);
        Add(list, "--message", message);
        Add(list, "--stage", stage);
        return list;
    }

    /// <summary>
    /// The shape shared by the four commands that can be narrowed to one assembly. A null or empty
    /// assembly means the whole solution, which is exactly what dv does with a missing -a.
    /// </summary>
    private static IReadOnlyList<string> Assembly(string verb, string assembly, string configuration)
    {
        var list = new List<string> { verb };
        Add(list, "-a", assembly);
        Add(list, "-c", configuration);
        return list;
    }

    private static void Add(List<string> arguments, string option, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(option);
        arguments.Add(value.Trim());
    }

    private static string Required(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A " + what + " is required.", what);
        }

        return value.Trim();
    }
}
