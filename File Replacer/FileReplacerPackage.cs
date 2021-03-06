﻿//------------------------------------------------------------------------------
// <copyright file="FileReplacerPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using EnvDTE80;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell.Design.Serialization;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using EnvDTE;
using System.Collections.Generic;

namespace File_Replacer
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid(FileReplacerPackage.PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]     // Load extension when any solution is opened
    public sealed class FileReplacerPackage : Package
    {
        // Document events
        private Lazy<DTE2> dte;
        private Lazy<EnvDTE.Events> events;
        private Lazy<EnvDTE.DocumentEvents> documentEvents;
        private Lazy<EnvDTE.BuildEvents> buildEvents;

        private Regex regex;
        private string[] filesToExclude = new string[] { "Web.config" };


        /// <summary>
        /// FileReplacerPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "5e325183-930f-46a7-9b52-f98186bb1d70";

        /// <summary>
        /// Initializes a new instance of the <see cref="FileReplacerPackage"/> class.
        /// </summary>
        public FileReplacerPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            // EnvDTE.Events
            dte = new Lazy<DTE2>(() => ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as DTE2);
            events = new Lazy<EnvDTE.Events>(() => dte.Value.Events);
            documentEvents = new Lazy<EnvDTE.DocumentEvents>(() => events.Value.DocumentEvents);
            buildEvents = new Lazy<EnvDTE.BuildEvents>(() => events.Value.BuildEvents);

            // Filename regex matcher
            regex = new Regex(@"(.*)\.(.*)\.(.*)");

            // Events subscriptions
            documentEvents.Value.DocumentSaved += DocumentEvents_DocumentSaved;
            buildEvents.Value.OnBuildBegin += BuildEvents_OnBuildBegin;
        }

        /// <summary>
        /// This event will run when any file is saved
        /// </summary>
        /// <param name="document">Saved document</param>
        private void DocumentEvents_DocumentSaved(EnvDTE.Document document)
        {
            // If this is a file we should use for replacement, let's use it
            if (regex.Match(document.Name).Success)
            {
                var groups = regex.Split(document.Name);
                string activeConfiguration = document.ProjectItem.ContainingProject.ConfigurationManager.ActiveConfiguration.ConfigurationName;
                string name = groups[1];
                string configName = groups[2];
                string extension = groups[3];

                // Check if we should exclude the file
                if (!filesToExclude.Any(f => f.Equals(name + "." + extension, StringComparison.InvariantCultureIgnoreCase)))
                {
                    // Only copy the file if it matches the current active configuration (the match is case insensitive)
                    if (configName.Equals(activeConfiguration, StringComparison.InvariantCultureIgnoreCase))
                    {
                        string sourceFile = document.FullName;
                        string destFile = Path.Combine(Path.GetDirectoryName(document.FullName), name + "." + extension);

                        // Replace the file
                        ReplaceFile(sourceFile, destFile, document.ProjectItem.ContainingProject);
                    }
                }
            }
        }

        /// <summary>
        /// This event will run before starting the Build process
        /// </summary>
        /// <param name="Scope"></param>
        /// <param name="Action"></param>
        private void BuildEvents_OnBuildBegin(vsBuildScope Scope, vsBuildAction Action)
        {
            // GetActiveProject() will fail if not building from a project, we will handle this later, by now, just emit a warning
            if (Scope != vsBuildScope.vsBuildScopeProject)
            {
                // Write to the build output pane
                OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, "Can't replace files when building from solution. Build project instead."
                    + Environment.NewLine);
                return;
            }

            // Write to the build output pane
            OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, "Replacing files..." + Environment.NewLine);

            // Before starting the build process, replace the needed files of the current active project
            Project project = GetActiveProject();

            string projectDir = Path.GetDirectoryName(project.FullName);
            string activeConfiguration = project.ConfigurationManager.ActiveConfiguration.ConfigurationName;
            string[] files = Directory.GetFiles(projectDir, "*", SearchOption.AllDirectories);
            int fileCount = 0;

            OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, "Active configuration is: " + activeConfiguration + Environment.NewLine);
            OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, files.Length + " files found in project " + project.Name + Environment.NewLine);

            // Scan every file in the project folder before building
            foreach (string file in files)
            {
                // If the file is a candidate for replacement let's check it
                string fileName = Path.GetFileName(file);
                if (regex.Match(fileName).Success)
                {
                    var groups = regex.Split(fileName);
                    string name = groups[1];
                    string configName = groups[2];
                    string extension = groups[3];

                    // Check if we should exclude the file
                    if (!filesToExclude.Any(f => f.Equals(name + "." + extension, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        // Only copy the file if it matches the current active configuration (the match is case insensitive)
                        if (configName.Equals(activeConfiguration, StringComparison.InvariantCultureIgnoreCase))
                        {
                            string sourceFile = file;
                            string destFile = Path.Combine(Path.GetDirectoryName(file), name + "." + extension);

                            // Replace the file
                            ReplaceFile(sourceFile, destFile, project);
                            ++fileCount;
                        }
                    } else
                    {
                        OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, "File " + file + " was excluded" + Environment.NewLine);
                    }
                }
            }

            OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid, fileCount + " file(s) replaced" + Environment.NewLine);
        }

        /// <summary>
        /// Replaces the source file with the destination file
        /// </summary>
        /// <param name="source">full path to the source file</param>
        /// <param name="dest">full path to the destination file</param>
        /// <param name="project">project where the file is located</param>
        private void ReplaceFile(string source, string dest, Project project)
        {
            bool documentOpened = false;

            OutputString(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid,
                "Replacing " + dest + " with " + source + Environment.NewLine);

            // Get source text. Make sure the project item is opened, otherwise the Document property
            //  will be null
            var projectItem = project.DTE.Solution.FindProjectItem(source);
            if (!projectItem.IsOpen)
            {
                projectItem.Open();
                documentOpened = true;
            }

            // Try to open the document as Text
            var textDocument = (TextDocument)projectItem.Document.Object("TextDocument");

            // Not a text document
            if (textDocument == null)
            {
                // If we can replace it as text, just copy the file manually
                File.Copy(source, dest, true);
            }
            // Replace the text document
            else
            {
                EditPoint editPoint = textDocument.StartPoint.CreateEditPoint();
                var content = editPoint.GetText(textDocument.EndPoint);

                // Copy the text to the destination file
                var docData = new DocData(this, dest);
                var editorAdaptersFactoryService = (this.GetService(typeof(SComponentModel)) as IComponentModel).GetService<IVsEditorAdaptersFactoryService>();
                var textBuffer = editorAdaptersFactoryService.GetDataBuffer(docData.Buffer);
                textBuffer.Replace(new Span(0, textBuffer.CurrentSnapshot.Length), content);

                // Save the file after modifying it
                project.DTE.Solution.FindProjectItem(dest).Save();
            }
            
            // Close the document if we opened it
            if (documentOpened) projectItem.Document.Close();
        }

        /// <summary>
        /// Get the active project
        /// </summary>
        /// <returns>Active project</returns>
        private Project GetActiveProject()
        {
            DTE2 dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            return GetActiveProject(dte);
        }

        private Project GetActiveProject(DTE2 dte)
        {
            Project activeProject = null;

            Array activeSolutionProjects = dte.ActiveSolutionProjects as Array;
            if (activeSolutionProjects != null && activeSolutionProjects.Length > 0)
            {
                activeProject = activeSolutionProjects.GetValue(0) as Project;
            }

            return activeProject;
        }

        /// <summary>
        /// Output string to the specified pane
        /// </summary>
        /// <param name="guidPane">GUID of the pane to use</param>
        /// <param name="text">Text to output</param>
        private void OutputString(Guid guidPane, string text)
        {
            const int VISIBLE = 1;
            const int DO_NOT_CLEAR_WITH_SOLUTION = 0;

            IVsOutputWindow outputWindow;
            IVsOutputWindowPane outputWindowPane = null;
            int hr;

            // Get the output window
            outputWindow = base.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;

            // The General pane is not created by default. We must force its creation
            if (guidPane == VSConstants.OutputWindowPaneGuid.GeneralPane_guid)
            {
                hr = outputWindow.CreatePane(guidPane, "General", VISIBLE, DO_NOT_CLEAR_WITH_SOLUTION);
                ErrorHandler.ThrowOnFailure(hr);
            }

            // Get the pane
            hr = outputWindow.GetPane(guidPane, out outputWindowPane);
            ErrorHandler.ThrowOnFailure(hr);

            // Output the text
            if (outputWindowPane != null)
            {
                outputWindowPane.OutputString(text);
                outputWindowPane.Activate();
            }
        }

        #endregion
    }
}
