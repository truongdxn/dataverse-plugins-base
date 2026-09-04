using System;
using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace Dataverse.Plugins.VsCommands;

/// <summary>What was right-clicked, reduced to the three things a dv command needs.</summary>
internal sealed class SolutionSelection
{
    public string ProjectName { get; private set; }

    /// <summary>The clicked project's folder, or the solution's when no project is selected.</summary>
    public string Directory { get; private set; }

    /// <summary>
    /// Visual Studio's active configuration, passed through as -c.
    /// <para>
    /// Taken from the IDE rather than fixed at Debug, so that switching the configuration dropdown
    /// changes what the menu builds. A developer who has selected Release and gets a Debug build
    /// has been lied to by the menu.
    /// </para>
    /// </summary>
    public string Configuration { get; private set; }

    /// <summary>
    /// Reads the current selection. Must be called on the UI thread - both because DTE demands it
    /// and because this is also used from BeforeQueryStatus, which is never off it.
    /// </summary>
    public static SolutionSelection From(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var selection = new SolutionSelection();

        if (dte == null)
        {
            return selection;
        }

        selection.Configuration = ActiveConfiguration(dte);

        var project = SelectedProject(dte);

        if (project != null)
        {
            selection.ProjectName = project.Name;
            selection.Directory = DirectoryOf(SafeFullName(project));
        }

        if (string.IsNullOrEmpty(selection.Directory))
        {
            selection.Directory = DirectoryOf(SolutionFile(dte));
        }

        return selection;
    }

    private static Project SelectedProject(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var selected = dte.SelectedItems;

            // One item, because these commands hang off a single project node. A multi-selection
            // is answered by acting on the first, which is what the menu appears to promise.
            if (selected != null && selected.Count > 0)
            {
                var item = selected.Item(1);

                if (item?.Project != null)
                {
                    return item.Project;
                }

                // A file was clicked rather than the project node; its owner is what was meant.
                if (item?.ProjectItem?.ContainingProject != null)
                {
                    return item.ProjectItem.ContainingProject;
                }
            }
        }
        catch (Exception)
        {
            // Solution Explorer is entitled to have nothing usable selected, and a solution folder
            // throws rather than returning null for some of these. Falling back to the solution
            // directory is always safe: dv then acts on the whole solution.
        }

        return null;
    }

    private static string SafeFullName(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return project.FullName;
        }
        catch (Exception)
        {
            // Solution folders and some project types throw on FullName rather than returning
            // empty, which would otherwise take down the whole query-status pass.
            return null;
        }
    }

    private static string SolutionFile(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return dte.Solution?.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ActiveConfiguration(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return dte.Solution?.SolutionBuild?.ActiveConfiguration?.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string DirectoryOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            // An open-folder workspace hands over a directory; a project hands over a file.
            return System.IO.Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
