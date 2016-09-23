﻿// Copyright 2016 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Google.Apis.Appengine.v1.Data;
using GoogleCloudExtension.CloudExplorer;
using GoogleCloudExtension.DataSources;
using GoogleCloudExtension.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoogleCloudExtension.CloudExplorerSources.Gae
{
    /// <summary>
    /// This class represents a view of a GAE version in the Google Cloud Explorer Window.
    /// </summary>
    class VersionViewModel : TreeHierarchy, ICloudExplorerItemSource
    {
        private const string IconRunningResourcePath = "CloudExplorerSources/Gae/Resources/instance_icon_running.png";
        private const string IconStopedResourcePath = "CloudExplorerSources/Gae/Resources/instance_icon_stoped.png";
        private const string IconTransitionResourcePath = "CloudExplorerSources/Gae/Resources/instance_icon_transition.png";

        private static readonly Lazy<ImageSource> s_versionRunningIcon = new Lazy<ImageSource>(() => ResourceUtils.LoadImage(IconRunningResourcePath));
        private static readonly Lazy<ImageSource> s_versionStopedIcon = new Lazy<ImageSource>(() => ResourceUtils.LoadImage(IconStopedResourcePath));
        private static readonly Lazy<ImageSource> s_versionTransitionIcon = new Lazy<ImageSource>(() => ResourceUtils.LoadImage(IconTransitionResourcePath));

        private static readonly TreeLeaf s_loadingPlaceholder = new TreeLeaf
        {
            Caption = Resources.CloudExplorerGaeLoadingInstancesCaption,
            IsLoading = true
        };
        private static readonly TreeLeaf s_noItemsPlacehoder = new TreeLeaf
        {
            Caption = Resources.CloudExplorerGaeNoInstancesFoundCaption,
            IsWarning = true
        };
        private static readonly TreeLeaf s_errorPlaceholder = new TreeLeaf
        {
            Caption = Resources.CloudExplorerGaeFailedToLoadInstancesCaption,
            IsError = true
        };

        private bool HasTrafficAllocation => trafficAllocation != null;

        private readonly ServiceViewModel _owner;

        public readonly GaeSourceRootViewModel root;

        private bool _resourcesLoaded;

        public Google.Apis.Appengine.v1.Data.Version version { get; private set; }

        public double? trafficAllocation { get; private set; }

        public event EventHandler ItemChanged;

        public object Item => GetItem();

        public VersionViewModel(
            ServiceViewModel owner, Google.Apis.Appengine.v1.Data.Version version)
        {
            _owner = owner;
            this.version = version;
            root = _owner.root;

            Initialize();
        }

        private void Initialize()
        {
            // Get the traffic allocation for the version
            trafficAllocation = GaeServiceExtensions.GetTrafficAllocation(_owner.service, version.Id);

            // Reset the resources loaded and clear any children. 
            _resourcesLoaded = false;
            Children.Clear();

            // If the version is serving allow instances to be loaded.
            if (GaeVersionExtensions.IsServing(version))
            {
                Children.Add(s_loadingPlaceholder);
            }

            // Update the view.
            Caption = GetCaption();
            UpdateIcon();
            UpdateMenu();
        }

        /// <summary>
        /// Update the context menu based on the current state of the version.
        /// </summary>
        private void UpdateMenu()
        {
            // Do not allow actions when the version is loading or in an error state.
            if (IsLoading || IsError)
            {
                ContextMenu = null;
                return;
            }

            var menuItems = new List<FrameworkElement>
            {
                new MenuItem { Header = Resources.UiOpenOnCloudConsoleMenuHeader, Command = new WeakCommand(OnOpenOnCloudConsoleCommand) },
                new MenuItem { Header = Resources.UiPropertiesMenuHeader, Command = new WeakCommand(OnPropertiesWindowCommand) },
            };

            // If the version has traffic allocated to it it can be opened.
            if (HasTrafficAllocation)
            {
                menuItems.Add(new MenuItem { Header = Resources.CloudExplorerGaeVersionOpen, Command = new WeakCommand(OnOpenVersion) });
            }

            menuItems.Add(new Separator());

            if (GaeVersionExtensions.IsServing(version))
            {
                menuItems.Add(new MenuItem { Header = Resources.CloudExplorerGaeStopVersion, Command = new WeakCommand(OnStopVersion) });
            }
            else if (GaeVersionExtensions.IsStopped(version))
            {
                menuItems.Add(new MenuItem { Header = Resources.CloudExplorerGaeStartVersion, Command = new WeakCommand(OnStartVersion) });
            }

            ContextMenu = new ContextMenu { ItemsSource = menuItems };
        }

        private void OnStartVersion()
        {
            UpdateServingStatus(
                GaeVersionExtensions.ServingStatus,
                Resources.CloudExplorerGaeVersionStartServingMessage);
        }

        private void OnStopVersion()
        {
            UpdateServingStatus(
                GaeVersionExtensions.StoppedStatus,
                Resources.CloudExplorerGaeVersionStopServingMessage);
        }

        protected override void OnIsExpandedChanged(bool newValue)
        {
            base.OnIsExpandedChanged(newValue);

            // If this is the first time the node has been expanded load it's resources.
            if (!_resourcesLoaded && newValue)
            {
                _resourcesLoaded = true;
                UpdateChildren();
            }
        }

        private void OnOpenOnCloudConsoleCommand()
        {
            var url = $"https://console.cloud.google.com/appengine/instances?project={root.Context.CurrentProject.ProjectId}&moduleId={_owner.service.Id}&versionId={version.Id}";
            Process.Start(url);
        }

        private void OnPropertiesWindowCommand()
        {
            root.Context.ShowPropertiesWindow(Item);
        }

        private void OnOpenVersion()
        {
            Process.Start(version.VersionUrl);
        }

        /// <summary>
        /// Update the serving status of the version.
        /// </summary>
        /// <param name="status">The serving status to update the version to.</param>
        /// <param name="statusMessage">The message to display while updating the status</param>
        private async void UpdateServingStatus(string status, string statusMessage)
        {
            IsLoading = true;
            Children.Clear();
            UpdateMenu();
            Caption = statusMessage;
            GaeDataSource datasource = root.DataSource.Value;

            try
            {
                Task<Operation> operationTask = datasource.UpdateVersionServingStatus(status, _owner.service.Id, version.Id);
                Func<Operation, Task<Operation>> fetch = (o) => datasource.GetOperationAsync(GaeVersionExtensions.GetOperationId(o));
                Predicate<Operation> stopPolling = (o) => o.Done ?? false;
                Operation operation = await Polling<Operation>.Poll(await operationTask, fetch, stopPolling);
                if (operation.Error != null)
                {
                    throw new DataSourceException(operation.Error.Message);
                }
                version = await datasource.GetVersionAsync(_owner.service.Id, version.Id);
            }
            catch (DataSourceException ex)
            {
                IsError = true;
                UserPromptUtils.ErrorPrompt(ex.Message,
                    Resources.CloudExplorerGaeUpdateServingStatusErrorMessage);
            }
            catch (TimeoutException ex)
            {
                IsError = true;
                UserPromptUtils.ErrorPrompt(
                    Resources.CloudExploreOperationTimeoutMessage,
                    Resources.CloudExplorerGaeUpdateServingStatusErrorMessage);
            }
            catch (OperationCanceledException ex)
            {
                IsError = true;
                UserPromptUtils.ErrorPrompt(
                    Resources.CloudExploreOperationCanceledMessage,
                    Resources.CloudExplorerGaeUpdateServingStatusErrorMessage);
            }
            finally
            {
                IsLoading = false;

                // Re-initialize the instance as it will have a new version.
                if (!IsError)
                {
                    Initialize();
                }
                else
                {
                    Caption = GetCaption();
                }
            }
        }

        /// <summary>
        /// Update the children (GAE instances) of this version.
        /// </summary>
        private async void UpdateChildren()
        {
            try
            {
                var instances = await LoadInstanceList();
                Children.Clear();
                if (instances == null)
                {
                    Children.Add(s_errorPlaceholder);
                }
                else
                {
                    foreach (var item in instances)
                    {
                        Children.Add(item);
                    }
                    if (Children.Count == 0)
                    {
                        Children.Add(s_noItemsPlacehoder);
                    }
                }
            }
            catch (DataSourceException ex)
            {
                GcpOutputWindow.OutputLine(Resources.CloudExplorerGaeFailedInstancesMessage);
                GcpOutputWindow.OutputLine(ex.Message);
                GcpOutputWindow.Activate();

                throw new CloudExplorerSourceException(ex.Message, ex);
            }
        }

        /// <summary>
        /// Load a list of instances.
        /// </summary>
        private async Task<List<InstanceViewModel>> LoadInstanceList()
        {
            var instances = await _owner.root.DataSource.Value.GetInstanceListAsync(_owner.service.Id, version.Id);
            return instances?.Select(x => new InstanceViewModel(this, x)).ToList();
        }

        /// <summary>
        /// Get a caption for a the version.
        /// Formated as 'versionId (traffic%)' if a traffic allocation is present, 'versionId' otherwise.
        /// </summary>
        private string GetCaption()
        {
            if (!HasTrafficAllocation)
            {
                return version.Id;
            }
            string percent = ((double)trafficAllocation).ToString("P", CultureInfo.InvariantCulture);
            return String.Format("{0} ({1})", version.Id, percent);
        }

        private void UpdateIcon()
        {
            switch (version.ServingStatus)
            {
                case GaeVersionExtensions.ServingStatus:
                    Icon = s_versionRunningIcon.Value;
                    break;
                case GaeVersionExtensions.StoppedStatus:
                    Icon = s_versionStopedIcon.Value;
                    break;
                default:
                    Icon = s_versionTransitionIcon.Value;
                    break;
            }
        }

        public VersionItem GetItem() => new VersionItem(version);
    }
}
