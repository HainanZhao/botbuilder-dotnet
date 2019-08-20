﻿// Licensed under the MIT License.
// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Bot.Builder.Dialogs.Declarative.Resources
{
    /// <summary>
    /// Folder/FileResources.
    /// </summary>
    public class FolderResourceProvider : IResourceProvider, IDisposable
    {
        private CancellationTokenSource cancelReloadToken = new CancellationTokenSource();
        private ConcurrentBag<string> changedPaths = new ConcurrentBag<string>();
        private FileSystemWatcher watcher;
        private Dictionary<string, FileResource> resources = new Dictionary<string, FileResource>();
        private HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FolderResourceProvider(string folder, bool includeSubFolders = true, bool monitorChanges = true)
        {
            foreach (var extension in new string[] { ".lg", ".lu", ".dialog", ".schema", ".md" })
            {
                this.extensions.Add(extension);
            }

            this.IncludeSubFolders = includeSubFolders;
            folder = PathUtils.NormalizePath(folder);
            this.Directory = new DirectoryInfo(folder);
            SearchOption option = this.IncludeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var fileInfo in this.Directory.EnumerateFiles($"*.*", option).Where(fi => this.extensions.Contains(fi.Extension)))
            {
                var fileResource = new FileResource(fileInfo.FullName);
                this.resources[fileResource.Id] = fileResource;
            }

            if (monitorChanges)
            {
                this.watcher = new FileSystemWatcher(folder);
                this.watcher.IncludeSubdirectories = this.IncludeSubFolders;
                this.watcher.EnableRaisingEvents = true;
                this.watcher.Created += Watcher_Changed;
                this.watcher.Changed += Watcher_Changed;
                this.watcher.Deleted += Watcher_Deleted;
                this.watcher.Renamed += Watcher_Renamed;
            }
        }

        public event ResourceChangedEventHandler Changed;

        /// <summary>
        /// Gets or sets folder to enumerate.
        /// </summary>
        /// <value>
        /// folder to enumerate.
        /// </value>
        public DirectoryInfo Directory { get; set; }

        public bool IncludeSubFolders { get; set; }

        public string Id
        {
            get { return this.Directory.FullName; }
        }

        public void Dispose()
        {
            lock (Directory)
            {
                if (this.watcher != null)
                {
                    this.watcher.EnableRaisingEvents = false;
                    this.watcher.Dispose();
                    this.watcher = null;
                }
            }
        }

        /// <summary>
        /// GetResource by id.
        /// </summary>
        /// <param name="id">Resource ID.</param>
        /// <returns>Resource.</returns>
        public IResource GetResource(string id)
        {
            lock (this.resources)
            {
                if (this.resources.TryGetValue(id, out FileResource fileResource))
                {
                    return fileResource;
                }

                return null;
            }
        }

        /// <summary>
        /// Get Resources by extension.
        /// </summary>
        /// <param name="extension">Resource extension.</param>
        /// <returns>Collection of resources.</returns>
        public IEnumerable<IResource> GetResources(string extension)
        {
            extension = $".{extension.TrimStart('.').ToLower()}";

            lock (this.resources)
            {
                return this.resources.Where(pair => Path.GetExtension(pair.Key).ToLower() == extension).Select(pair => pair.Value).ToList();
            }
        }

        public override string ToString()
        {
            return this.Id;
        }

        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            if (this.extensions.Contains(ext))
            {
                lock (this.resources)
                {
                    this.resources.Remove(Path.GetFileName(e.FullPath));
                    if (this.Changed != null)
                    {
                        this.Changed(new IResource[] { new FileResource(e.FullPath) });
                    }
                }
            }
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            if (this.extensions.Contains(ext))
            {
                lock (this.resources)
                {
                    var fileResource = new FileResource(e.FullPath);
                    this.resources[fileResource.Id] = fileResource;
                    if (this.Changed != null)
                    {
                        this.Changed(new IResource[] { fileResource });
                    }
                }
            }
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            if (this.extensions.Contains(ext))
            {
                var fileResource = new FileResource(e.FullPath);

                lock (this.resources)
                {
                    this.resources[fileResource.Id] = fileResource;
                }

                if (this.Changed != null)
                {
                    this.Changed(new IResource[] { fileResource });
                }
            }
        }
    }

    public static class FolderResourceProviderExtensions
    {
        /// <summary>
        /// Add a folder resource.
        /// </summary>
        /// <param name="explorer">The <see cref="ResourceExplorer"/> for this extension method.</param>
        /// <param name="folder">Folder to include as a resource.</param>
        /// <param name="includeSubFolders">Whether to include subfolders.</param>
        /// <param name="monitorChanges">Whether to track changes.</param>
        /// <returns>The resource explorer.</returns>
        public static ResourceExplorer AddFolder(this ResourceExplorer explorer, string folder, bool includeSubFolders = true, bool monitorChanges = true)
        {
            explorer.AddResourceProvider(new FolderResourceProvider(folder, includeSubFolders: includeSubFolders, monitorChanges: monitorChanges));
            return explorer;
        }

        /// <summary>
        ///  Add folder resources.
        /// </summary>
        /// <param name="explorer">The <see cref="ResourceExplorer"/> for this extension method.</param>
        /// <param name="folder">Collection of folders to include as resources.</param>
        /// <param name="ignoreFolders">Folders to ignore.</param>
        /// <param name="monitorChanges">Whether to track changes.</param>
        /// <returns>The resource explorer.</returns>
        public static ResourceExplorer AddFolders(this ResourceExplorer explorer, string folder, string[] ignoreFolders = null, bool monitorChanges = true)
        {
            if (ignoreFolders != null)
            {
                folder = PathUtils.NormalizePath(folder);

                explorer.AddFolder(folder, includeSubFolders: false, monitorChanges: monitorChanges);

                var hashIgnore = new HashSet<string>(ignoreFolders.Select(p => Path.Combine(folder, p)), StringComparer.CurrentCultureIgnoreCase);
                foreach (var subFolder in Directory.GetDirectories(folder).Where(f => !hashIgnore.Contains(f)))
                {
                    // add subfolders not in ignore list
                    explorer.AddFolder(subFolder, includeSubFolders: true, monitorChanges: monitorChanges);
                }
            }
            else
            {
                explorer.AddFolder(folder, includeSubFolders: true, monitorChanges: monitorChanges);
            }

            return explorer;
        }
    }
}
