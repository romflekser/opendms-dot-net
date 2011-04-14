﻿/* Copyright 2011 the OpenDMS.NET Project (http://sites.google.com/site/opendmsnet/)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections;
using System.ComponentModel;
using System.IO;
using Common.Storage;
using Common.Work;

namespace WindowsClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, Common.Work.IWorkRequestor
    {
        /// <summary>
        /// This property is a temporary placeholder for a username
        /// </summary>
        public const string TEMP_USERNAME = "lucas";

        /// <summary>
        /// Represents the method that handles a file system event
        /// </summary>
        /// <param name="guid">The GUID.</param>
        delegate void FileSystemEventDelegate(Guid guid);

        /// <summary>
        /// Represents the method that handles a resource event.
        /// </summary>
        /// <param name="tvi">The <see cref="TreeViewItem"/>.</param>
        /// <param name="resource">The resource.</param>
        delegate void ResourceDelegate(TreeViewItem tvi, Resource resource);

        /// <summary>
        /// The brush for outdated assets.
        /// </summary>
        Brush _outdatedBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0)); // Yellow
        /// <summary>
        /// The brush for assets in a state of error.
        /// </summary>
        Brush _errorBrush = new SolidColorBrush(Color.FromArgb(75, 255, 0, 0)); // Red
        /// <summary>
        /// The brush for assets that need saved to the server.
        /// </summary>
        Brush _needUpdatedBrush = new SolidColorBrush(Color.FromArgb(75, 0, 255, 0)); // Green
        /// <summary>
        /// The brush for assets normally.
        /// </summary>
        Brush _normalBrush = Brushes.Transparent;

        /// <summary>
        /// The <see cref="Guid"/> of the item whos properties are currently being displayed in the status bar.
        /// </summary>
        Guid _statusBarItemGuid;
        /// <summary>
        /// A reference to the <see cref="Master"/>.
        /// </summary>
        Master _workMaster;
        /// <summary>
        /// A reference to the <see cref="FileSystemWatcher"/> monitoring the file system.
        /// </summary>
        FileSystemWatcher _fsWatcher;

        Common.CouchDB.Database _couchdb;
        Dictionary<string, Guid> _addedFileMappings;

        /// <summary>
        /// Provides Guid translation for different ids being received than are transmitted.
        /// </summary>
        /// <remarks>When an asset is loaded into the client, the client assigns it a random Guid and 
        /// knows it as this Guid only.  When the asset is saved to the server, the server ignores the 
        /// Guid assigned by the client, assigning its own Guid.  This property exists to allow the 
        /// client to match the key (the client assigned Guid) to determine the value (the server 
        /// assigned value).  This is horridly ugly and embarrassing, please fix me.</remarks>
        public static Dictionary<Guid, Guid> IdTranslation;

        /// <summary>
        /// A reference to a global <see cref="ErrorManager"/>.
        /// </summary>
        public static Common.ErrorManager ErrorManager;
        /// <summary>
        /// A reference to a global <see cref="Common.FileSystem.IO"/>.
        /// </summary>
        public static Common.FileSystem.IO FileSystem;
        /// <summary>
        /// A reference to a global set of loggers to document events.
        /// </summary>
        public static Common.Logger Logger;




        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Settings should come first
            Settings.Instance = Settings.Load(Utilities.GetAppDataPath() + "Settings.xml");
            if (Settings.Instance == null)
            {
                SettingsWindow win = new SettingsWindow();
                Settings.Instance = new Settings();
                win.ShowDialog();
            }

            // File System must after settings
            FileSystem = new Common.FileSystem.IO(Settings.Instance.StorageLocation);

            Logger = new Common.Logger(Utilities.GetAppDataPath());

            Common.ErrorManager.UpdateUI actErrorUpdateUI = ErrorUpdateUI;

            ErrorManager = new Common.ErrorManager(actErrorUpdateUI);
            _statusBarItemGuid = Guid.Empty;
            _workMaster = new Master(ErrorManager, FileSystem);
            _fsWatcher = new FileSystemWatcher(Settings.Instance.StorageLocation);
            _fsWatcher.IncludeSubdirectories = true;
            _fsWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _fsWatcher.Changed += new FileSystemEventHandler(FS_Changed);
            _fsWatcher.EnableRaisingEvents = true;
            _addedFileMappings = new Dictionary<string, Guid>();
            this.Loaded += new RoutedEventHandler(MainWindow_Loaded);
        }

        /// <summary>
        /// Called when errors occur in other threads.
        /// </summary>
        /// <param name="errors">The errors.</param>
        void ErrorUpdateUI(List<Common.ErrorMessage> errors)
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// Handles the Loaded event of the MainWindow control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        void MainWindow_Loaded(object sender, RoutedEventArgs args)
        {
            ResourceTree.Items.Clear();
            //CreateTestSearchForm();
            //CreateTestResources();

            LoadLocalResources();
        }

        /// <summary>
        /// Called by <see cref="M:FS_Changed"/> to process actions on the UI thread.
        /// </summary>
        /// <param name="guid">The <see cref="Guid"/> of the resource.</param>
        /// <remarks>
        /// This method is executed on the UI thread.
        /// </remarks>
        private void FS_ChangedOnUI(Guid guid)
        {
            TreeViewItem tvi;

            if ((tvi = FindTreeViewItem(guid)) != null)
            {
                ((TVIState)tvi.Tag).UpdateResourceStatus(true, false, false, null, true);
                tvi.Background = _needUpdatedBrush;
                UpdateStatus(tvi, "File needs saved to server");
            }
        }

        /// <summary>
        /// Called by the <see cref="FileSystemWatcher"/> when a file or directory within the monitored path is changed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.IO.RenamedEventArgs"/> instance containing the event data.</param>
        /// <remarks>This method is called on a background thread.</remarks>
        void FS_Changed(object sender, FileSystemEventArgs e)
        {
            Guid guid;

            // If not change type, get out
            if (e.ChangeType != WatcherChangeTypes.Changed)
                return;

            // If not existing still then get out
            if (!FileSystem.ResourceExists(FileSystem.GetRelativePathFromFullPath(e.FullPath)))
                return;

            // If not a guid then get out
            // TODO: This relies on exceptions when files are not resources...  This needs to attempt some 
            // type of regex check first then rely on parsing
            if (!Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(e.Name), out guid))
                return;

            FileSystemEventDelegate actUpdateUI = FS_ChangedOnUI;
            Dispatcher.BeginInvoke(actUpdateUI, guid);
        }
        
        /// <summary>
        /// Creates the test search form and saves it to the local file system.
        /// </summary>
        void CreateTestSearchForm()
        {
            Common.NetworkPackage.SearchForm sf = new Common.NetworkPackage.SearchForm();

            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Text, "Id", "$guid", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Text, "Extension", "$extension", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.TextCollection, "Tags", "$tags", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Date, "Created", "$created", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Date, "Modified", "$modified", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Date, "Last Access", "$lastaccess", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Text, "User Field 1", "prop1", ""));
            sf.Add(new Common.NetworkPackage.FormProperty(Common.NetworkPackage.FormProperty.SupportedDataType.Date, "User Field 2", "prop2", ""));

            sf.SaveToFile("settings\\searchform.xml", FileSystem, true);
        }

        /// <summary>
        /// Loads all resources on the local file system.
        /// </summary>
        /// <remarks>Runs on the UI thread.</remarks>
        void LoadLocalResources()
        {
            MetaAsset ma;
            Resource resource = null;
            Guid guid = Guid.Empty;
            string temp;

            CheckUpdateStatusJob.UpdateUIDelegate actUpdateUI = CheckUpdateStatus;
            string[] files = FileSystem.GetFiles(Common.FileSystem.Path.RelativeMetaPath);

            for (int i = 0; i < files.Length; i++)
            {
                temp = files[i];
                files[i] = System.IO.Path.GetFileNameWithoutExtension(files[i]);

                try { guid = new Guid(files[i]); }
                catch
                {
                    guid = Guid.Empty;
                }

                if (guid != Guid.Empty)
                {
                    ma = new MetaAsset(guid, _couchdb);

                    if (ma.LoadFromLocal(null, ma.RelativePath, FileSystem))
                    {
                        resource = new Resource(ma, _couchdb);
                        _workMaster.AddJob(this, Master.JobType.CheckUpdateStatus, resource, actUpdateUI, 100000);
                    }
                    else
                    {
                        MessageBox.Show("The resource with id " + guid.ToString("N") + " failed to load, please verify the formatting of its meta data.");
                    }
                }
            }
        }

        /// <summary>
        /// Calls <see cref="M:CheckHeadStatus(GetHeadJob job, FullAsset fullAsset)"/>.
        /// </summary>
        /// <param name="job">The <see cref="JobBase"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void CheckUpdateStatus(JobBase job, Resource resource)
        {
            CheckUpdateStatus((CheckUpdateStatusJob)job, (Resource)resource);
        }

        /// <summary>
        /// Called when a <see cref="GetHeadJob"/> has terminated.
        /// </summary>
        /// <param name="job">The <see cref="GetHeadJob"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void CheckUpdateStatus(CheckUpdateStatusJob job, Resource resource)
        {
            string localResourceMd5 = null;
            TVIState tviState;
            TreeViewItem tvi = FindTreeViewItem(resource);

            if (tvi == null)
            {
                // Being loaded without being present in the tree (loaded from application startup)
                if (job.IsCancelled)
                {
                    MessageBox.Show("Actions on resource " + job.Resource.Guid.ToString("N") + " were cancelled by the user.");
                }
                else if (job.IsError)
                {
                    MessageBox.Show("An error occurred while trying to check the status of the resource " + job.Resource.Guid.ToString("N") + ".");
                }
                else if (job.IsFinished)
                {
                    tvi = AddTreeResource(resource, false, true);
                    tviState = (TVIState)tvi.Tag;
                    TreeViewItemProps.SetPercentComplete(tvi, 100);
                    TreeViewItemProps.SetIsLoading(tvi, false);
                    localResourceMd5 = new Common.FileSystem.MetaResource(resource.Guid, FileSystem).ComputeMd5();

                    // If the local resource does not match the Md5 value of the remote resource, then...
                    if (job.Resource.MetaAsset.Md5 != localResourceMd5)
                    {
                        // The local version is newer
                        tvi.Background = _needUpdatedBrush;
                        tviState.UpdateResourceStatus(true, false, false, true, true);
                        UpdateStatus(tvi, "File needs saved to server");
                    }
                    else
                    {
                        UpdateStatus(tvi, "Loaded");
                    }
                }
                else if (job.IsTimeout)
                {
                    MessageBox.Show("Actions on resource " + job.Resource.Guid.ToString("N") + " timed out.");
                }
                else if (job.IsError)
                {

                }
                else
                {
                    throw new Exception("Unhandled event");
                }
            }
            else
            {
                tviState = (TVIState)tvi.Tag;
                TreeViewItemProps.SetIsLoading(tvi, false);

                if (job.IsCancelled)
                {
                    TreeViewItemProps.SetIsCanceled(tvi, true);
                    tviState.UpdateEvent(false, false, true, false, false);
                    UpdateStatus(tvi, "Action cancelled by user");
                }
                else if (job.IsFinished)
                {
                    tvi.Background = _normalBrush;
                    tviState.UpdateEvent(false, true, false, false, false);
                    TreeViewItemProps.SetPercentComplete(tvi, 100);
                    localResourceMd5 = new Common.FileSystem.MetaResource(resource.Guid, FileSystem).ComputeMd5();

                    // If the local resource does not match the Md5 value of the remote resource, then...
                    if (job.Resource.MetaAsset.Md5 != localResourceMd5)
                    {
                        // The local version is newer
                        tvi.Background = _needUpdatedBrush;
                        tviState.UpdateResourceStatus(true, false, false, true, true);
                        UpdateStatus(tvi, "File needs saved to server");
                    }
                    else
                    {
                        UpdateStatus(tvi, "Loaded");
                    }
                }
                else if (job.IsTimeout)
                {
                    tvi.Background = _errorBrush;
                    tviState.UpdateEvent(false, false, false, true, false);
                    UpdateStatus(tvi, "Error: Timeout");
                }
                else if (job.IsError)
                {
                    tviState.UpdateEvent(false, false, false, false, true);
                    UpdateStatus(tvi, "Error");
                }
                else
                {
                    throw new Exception("Unhandled event");
                }
            }
        }
        
        /// <summary>
        /// Adds a Resource to the TreeView.
        /// </summary>
        /// <param name="fullAsset">The <see cref="FullAsset"/> to add to the TreeView.</param>
        /// <param name="isLoading">If set to <c>true</c> the resource is loading from the remote host.</param>
        /// <param name="isLoaded">True if the Resource is considered loaded (up-to-date)</param>
        /// <returns>
        /// A TreeViewItem representing the same as added to the TreeView
        /// </returns>
        /// <remarks>Runs on the UI thread.</remarks>
        TreeViewItem AddTreeResource(Resource resource, bool isLoading, bool isLoaded)
        {
            TreeViewItem tvi;
            System.Windows.Controls.ContextMenu menu = new System.Windows.Controls.ContextMenu();

            tvi = new TreeViewItem();
            tvi.Selected += new RoutedEventHandler(TreeViewItem_Selected);
            tvi.Header = resource.Guid.ToString("N");
            tvi.Tag = new TVIState(resource);
            ((TVIState)tvi.Tag).UpdateEvent(isLoading, isLoaded, false, false, false);
            TreeViewItemProps.SetGuid(tvi, resource.Guid.ToString("N"));
            TreeViewItemProps.SetIsLoading(tvi, isLoading);
            TreeViewItemProps.SetIsCanceled(tvi, false);
            if (isLoaded)
            {
                TreeViewItemProps.SetPercentComplete(tvi, 100);
                UpdateStatus(tvi, "Loaded");
            }
            else
            {
                TreeViewItemProps.SetPercentComplete(tvi, 0);
                UpdateStatus(tvi, "Not Loaded");
            }

            tvi.ContextMenu = new System.Windows.Controls.ContextMenu();
            tvi.ContextMenu.PlacementTarget = tvi;
            tvi.ContextMenu.IsOpen = false;


            //System.Windows.Controls.MenuItem mi1 = new MenuItem();
            //mi1.Header = "Lock";
            //mi1.Click += new RoutedEventHandler(MenuItem_Click);
            //System.Windows.Controls.MenuItem mi2 = new MenuItem();
            //mi2.Header = "Unlock";
            //mi2.Click += new RoutedEventHandler(MenuItem_Click);
            System.Windows.Controls.MenuItem mi3 = new MenuItem();
            mi3.Header = "Release";
            mi3.Click += new RoutedEventHandler(MenuItem_Click);


            //tvi.ContextMenu.Items.Add(mi1);
            //tvi.ContextMenu.Items.Add(mi2);
            tvi.ContextMenu.Items.Add(mi3);

            ResourceTree.Items.Add(tvi);
            
            return tvi;
        }

        /// <summary>
        /// Handles the Click event of the MenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            TreeViewItem tvi;
            MenuItem mi = (MenuItem)sender;
            DependencyObject dobj = VisualTreeHelper.GetParent((DependencyObject)sender);

            while (dobj.GetType() != typeof(System.Windows.Controls.ContextMenu))
            {
                dobj = VisualTreeHelper.GetParent((DependencyObject)dobj);
            }

            tvi = (TreeViewItem)((System.Windows.Controls.ContextMenu)dobj).PlacementTarget;

            switch ((string)mi.Header)
            {
                case "Release": 
                    // Releases the lock on the resource at the server and removes the resource from 
                    // the local filesystem also removing it from the ResourceTree
                    ReleaseResource(tvi);
                    break;
                //case "Lock":
                //    // Applies a lock on the resource at the server and downloads an updated MetaAsset
                //    LockResource(tvi);
                //    break;
                //case "Unlock":
                //    // Releases a lock on the resource at the server and downloads an updated MetaAsset
                //    UnlockResource(tvi);
                //    break;
                default:
                    throw new Exception("Unknown context menu item.");
            }
        }

        /// <summary>
        /// Releases the resource - unlocks the resource on the server and removes it from the local client.
        /// </summary>
        /// <param name="tvi">The <see cref="TreeViewItem"/>.</param>
        private void ReleaseResource(TreeViewItem tvi)
        {
            TVIState tviState = (TVIState)tvi.Tag;
            LockJob.UpdateUIDelegate actUpdateUI = ReleaseResourceCallback;
            _workMaster.AddJob(this, Master.JobType.Unlock, tviState.Resource, actUpdateUI, 10000);
        }

        /// <summary>
        /// Calls <see cref="M:ReleaseResourceCallback(UnlockJob, FullAsset)"/>.
        /// </summary>
        /// <param name="job">The <see cref="JobBase"/>.</param>
        /// <param name="resource">The <see cref="Resource"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void ReleaseResourceCallback(JobBase job, Resource resource)
        {
            ReleaseResourceCallback((UnlockJob)job, resource);
        }

        /// <summary>
        /// Called when a job has terminated on a Resource.
        /// </summary>
        /// <param name="job">The <see cref="UnlockJob"/>.</param>
        /// <param name="resource">The <see cref="Resource"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void ReleaseResourceCallback(UnlockJob job, Resource resource)
        {
            TVIState tviState;
            TreeViewItem tvi;

            if ((tvi = FindTreeViewItem((Resource)resource)) == null)
            {
                Common.Logger.General.Error("Unable to locate the resource in the GUI tree.");
                MessageBox.Show("A request was received releasing a resource on the server, but I am unable to locate the resource.", "Resource not Found");
                return;
            }

            tviState = (TVIState)tvi.Tag;
            tviState.Resource = job.Resource;

            if (job.IsFinished)
            {
                if (ResourceTree.Items.Contains(tvi))
                    ResourceTree.Items.Remove(tvi);

                FileSystem.Delete(job.Resource.MetaAsset.RelativePath);
                FileSystem.Delete(job.Resource.DataAsset.RelativePath);
            }
        }

        /// <summary>
        /// Handles the Selected event of the TreeViewItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            TreeViewItem tvi = (TreeViewItem)sender;
            TVIState tviState = (TVIState)tvi.Tag;

            _statusBarItemGuid = tviState.Resource.Guid;
            SBItem.Content = TreeViewItemProps.GetStatus(tvi);

            // Outdated Brush = Local resource is outdated (older than remote)
            // Error Brush = Something bad happened and the last action failed
            // Need Updated Brush = Local resource is newer than remote and needs saved to the server
            // Normal Brush = Local matches remote

            /* if outdated -> disable save, enable get
             * else if error...
             * We ran into a problem, if error, what was the previous state???
             * Need to implement a new state tracking class.
             */

            if (tviState.IsLocalOlder)
            {
                BtnSaveSelected.IsEnabled = false;
                BtnGetSelected.IsEnabled = true;
            }
            else if (tviState.IsLocalNewer)
            {
                BtnSaveSelected.IsEnabled = true;
                BtnGetSelected.IsEnabled = false;
            }
            else if (tviState.IsLocalSameAsRemote)
            {
                BtnSaveSelected.IsEnabled = false;
                BtnGetSelected.IsEnabled = false;
            }
            else
                throw new Exception("Unknown state");

        }

        /// <summary>
        /// Handles the Click event of the btnCancelLoad control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void btnCancelLoad_Click(object sender, RoutedEventArgs e)
        {
            Button btnSender = (Button)e.OriginalSource;
            if (btnSender != null)
            {
                TreeViewItem tviOwner = (TreeViewItem)btnSender.Tag;
                if (tviOwner != null)
                {
                    if (tviOwner.Tag != null)
                    {
                        Resource resource = ((TVIState)tviOwner.Tag).Resource;
                        UpdateStatus(tviOwner, "Cancelling...");
                        tviOwner.Background = _errorBrush;
                        _workMaster.CancelJobForResource(resource);
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Click event of the btnReload control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void btnReload_Click(object sender, RoutedEventArgs e)
        {
            Button btnSender = (Button)e.OriginalSource;
            CheckUpdateStatusJob.UpdateUIDelegate actUpdateUI = CheckUpdateStatus;
            if (btnSender != null)
            {
                TreeViewItem tviOwner = (TreeViewItem)btnSender.Tag;
                if (tviOwner != null)
                {
                    if (tviOwner.Tag != null)
                    {
                        TVIState tviState = (TVIState)tviOwner.Tag;
                        Resource resource = tviState.Resource;
                        TreeViewItemProps.SetIsCanceled(tviOwner, false);
                        TreeViewItemProps.SetIsLoading(tviOwner, true);
                        TreeViewItemProps.SetStatus(tviOwner, "Starting reload...");
                        tviState.UpdateEvent(true, false, false, false, false);
                        tviOwner.Background = _normalBrush;
                        _workMaster.AddJob(this, Master.JobType.CheckUpdateStatus, resource, actUpdateUI, 
                            (uint)Settings.Instance.NetworkTimeout);
                    }
                }
            }
        }

        /// <summary>
        /// Calls <see cref="M:LoadResourceCallback(LoadResourceJob, FullAsset)"/>.
        /// </summary>
        /// <param name="job">The <see cref="JobBase"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void GetResourceCallback(JobBase job, Resource resource)
        {
            GetResourceCallback((GetResourceJob)job, resource);
        }

        /// <summary>
        /// Called when a job has terminated on a Resource.
        /// </summary>
        /// <param name="job">The <see cref="LoadResourceJob"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void GetResourceCallback(GetResourceJob job, Resource resource)
        {
            TVIState tviState;
            TreeViewItem tvi;

            if ((tvi = FindTreeViewItem(resource)) == null)
            {
                tvi = AddTreeResource(resource, true, false);
            }

            tviState = (TVIState)tvi.Tag;
            
            if (job.IsCancelled)
            {
                tviState.UpdateEvent(false, false, true, false, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Download was canceled by user");
            }
            else if (job.IsFinished)
            {
                // This could be used multiple times, depending on threading, should check a flag before making changes
                if(!tviState.IsLoaded)
                {
                    tvi.Background = _normalBrush;
                    tviState.UpdateEvent(false, true, false, false, false);
                    tviState.UpdateResourceStatus(false, false, true, true, true);
                    TreeViewItemProps.SetIsLoading(tvi, false);
                    TreeViewItemProps.SetIsCanceled(tvi, false);
                    TreeViewItemProps.SetPercentComplete(tvi, 100);
                    UpdateStatus(tvi, "Loaded");
                }
            }
            else if (job.IsTimeout)
            {
                tvi.Background = _errorBrush;
                tviState.UpdateEvent(false, false, false, true, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Error: Timeout");
            }
            else
            {
                TreeViewItemProps.SetPercentComplete(tvi, job.PercentComplete);
                UpdateStatus(tvi, "Downloading resource is " + job.PercentComplete.ToString() + "% complete, " +
                                Utilities.MakeBytesHumanReadable(job.BytesComplete) + " of " +
                                Utilities.MakeBytesHumanReadable(job.BytesTotal) + " have been downloaded.");
            }
        }

        /// <summary>
        /// Handles the Click event of the BtnGetSelected control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnGetSelected_Click(object sender, RoutedEventArgs e)
        {
            TVIState tviState;
            TreeViewItem tvi;
            Resource resource;
            GetResourceJob.UpdateUIDelegate actUpdateUI = GetResourceCallback;

            if (ResourceTree.SelectedItem == null)
            {
                MessageBox.Show("You must select a resource first.");
                return;
            }

            tvi = (TreeViewItem)ResourceTree.SelectedItem;

            if (tvi.Tag != null)
            {
                tviState = (TVIState)tvi.Tag;
                resource = tviState.Resource;
                tviState.UpdateEvent(true, false, false, false, false);
                tviState.UpdateResourceStatus(null, null, null, true, true);
                TreeViewItemProps.SetIsCanceled(tvi, false);
                TreeViewItemProps.SetIsLoading(tvi, true);
                tvi.Background = _normalBrush;
                _workMaster.AddJob(this, Master.JobType.GetResource, resource, actUpdateUI, 150000);
            }
        }

        /// <summary>
        /// Calls <see cref="M:CreateResourceCallback(CreateResourceJob, FullAsset)"/>.
        /// </summary>
        /// <param name="job">The <see cref="JobBase"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void CreateResourceCallback(JobBase job, Resource resource)
        {
            CreateResourceCallback((CreateResourceJob)job, resource);
        }

        /// <summary>
        /// Called when the <see cref="CreateResourceJob"/> has terminated.
        /// </summary>
        /// <param name="job">The <see cref="SaveResourceJob"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void CreateResourceCallback(CreateResourceJob job, Resource resource)
        {
            TVIState tviState = null;
            TreeViewItem tvi, foundTvi;
            
            // primary scan of all TVI's current guids
            if ((tvi = FindTreeViewItem((Resource)resource)) == null)
            {
                // Creation is weird on the client.  The client currently has the user add an existing 
                // resource to the client's repository.  The client assigns a Guid.  This Guid is used 
                // to reference the resource until which time that it is saved to the server.  During
                // creation of the resource on the server, the server assigns a new Guid to the 
                // resource.  Accordingly, the client must then reassign the resource to use the new 
                // Guid.  Thus, the following scan must be done to look at the previousguid property
                // of the FullAsset.

                for (int i = 0; i < ResourceTree.Items.Count; i++)
                {
                    foundTvi = (TreeViewItem)ResourceTree.Items[i];
                    if (foundTvi.Tag != null) // If null, then Tag has not been set, which means that LoadResource has not been called and thus, it cannot be the one we want
                    {
                        if (((TVIState)foundTvi.Tag).Resource == resource)
                        {
                            tvi = foundTvi;
                            tviState = (TVIState)tvi.Tag;
                            TreeViewItemProps.SetGuid(tvi, job.Resource.Guid.ToString("N"));
                            tvi.Header = job.Resource.Guid.ToString("N");
                            _statusBarItemGuid = job.Resource.Guid;
                            break;
                        }
                    }
                }
            }
            else
                tviState = (TVIState)tvi.Tag;

            if (tvi == null)
            {
                Common.Logger.General.Error("Unable to locate the resource in the GUI tree.");
                MessageBox.Show("A request was received regarding the status of a resource to be created on the server, but I am unable to locate the resource.", "Resource not Found");
                return;
            }


            // Update the tviState.FullAsset reference
            tviState.Resource = job.Resource;

            if (job.IsCancelled)
            {
                tviState.UpdateEvent(false, true, true, false, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Upload was canceled by user");
            }
            else if (job.IsFinished)
            {
                tvi.Background = _normalBrush;
                tviState.UpdateEvent(false, true, false, false, false);
                tviState.UpdateResourceStatus(false, false, true, true, true);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, false);
                TreeViewItemProps.SetPercentComplete(tvi, 100);
                UpdateStatus(tvi, "Loaded");
            }
            else if (job.IsTimeout)
            {
                tvi.Background = _errorBrush;
                tviState.UpdateEvent(false, true, false, true, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Error: Timeout");
            }
            else
            {
                TreeViewItemProps.SetPercentComplete(tvi, job.PercentComplete);
                UpdateStatus(tvi, "Uploading resource is " + job.PercentComplete.ToString() + "% complete, " +
                                Utilities.MakeBytesHumanReadable(job.BytesComplete) + " of " +
                                Utilities.MakeBytesHumanReadable(job.BytesTotal) + " have been uploaded.");
            }
        }

        /// <summary>
        /// Calls <see cref="M:SaveResourceCallback(SaveResourceJob, FullAsset)"/>.
        /// </summary>
        /// <param name="job">The <see cref="JobBase"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void SaveResourceCallback(JobBase job, Resource resource)
        {
            SaveResourceCallback((SaveResourceJob)job, resource);
        }

        /// <summary>
        /// Called when the <see cref="SaveResourceJob"/> has terminated.
        /// </summary>
        /// <param name="job">The <see cref="SaveResourceJob"/>.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void SaveResourceCallback(SaveResourceJob job, Resource resource)
        {
            TVIState tviState;
            TreeViewItem tvi;

            if ((tvi = FindTreeViewItem((Resource)resource)) == null)
            {
                tvi = AddTreeResource((Resource)resource, true, false);
            }

            tviState = (TVIState)tvi.Tag;

            // Update the tviState.FullAsset reference
            tviState.Resource = job.Resource;

            if (job.IsCancelled)
            {
                tviState.UpdateEvent(false, true, true, false, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Upload was canceled by user");
            }
            else if (job.IsFinished)
            {
                // This could be used multiple times, depending on threading, should check a flag before making changes
                if (!tviState.IsLoaded)
                {
                    tvi.Background = _normalBrush;
                    tviState.UpdateEvent(false, true, false, false, false);
                    tviState.UpdateResourceStatus(false, false, true, true, true);
                    TreeViewItemProps.SetIsLoading(tvi, false);
                    TreeViewItemProps.SetIsCanceled(tvi, false);
                    TreeViewItemProps.SetPercentComplete(tvi, 100);
                    UpdateStatus(tvi, "Loaded");
                }
            }
            else if (job.IsTimeout)
            {
                tvi.Background = _errorBrush;
                tviState.UpdateEvent(false, false, false, true, false);
                TreeViewItemProps.SetIsLoading(tvi, false);
                TreeViewItemProps.SetIsCanceled(tvi, true);
                UpdateStatus(tvi, "Error: Timeout");
            }
            else
            {
                TreeViewItemProps.SetPercentComplete(tvi, job.PercentComplete);
                UpdateStatus(tvi, "Uploading resource is " + job.PercentComplete.ToString() + "% complete, " +
                                Utilities.MakeBytesHumanReadable(job.BytesComplete) + " of " +
                                Utilities.MakeBytesHumanReadable(job.BytesTotal) + " have been uploaded.");
            }
        }

        /// <summary>
        /// Handles the Click event of the BtnSaveSelected control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnSaveSelected_Click(object sender, RoutedEventArgs e)
        {
            TreeViewItem tvi;
            MetaPropWindow win;
            
            
            if (ResourceTree.SelectedItem == null)
            {
                MessageBox.Show("You must select a resource first.");
                return;
            }

            tvi = (TreeViewItem)ResourceTree.SelectedItem;

            if(tvi.Tag == null)
            {
                MessageBox.Show("Invalid Tag value.");
            }

            win = new MetaPropWindow(((TVIState)tvi.Tag).Resource.Guid, FileSystem, _couchdb);

            if (!win.ShowDialog().Value)
                return;
             
            // Reload the meta
            ((TVIState)tvi.Tag).Resource.MetaAsset.LoadFromLocal(null,
                Common.FileSystem.Path.RelativeMetaPath +
                ((TVIState)tvi.Tag).Resource.Guid + ".xml", FileSystem);

            ((TVIState)tvi.Tag).UpdateEvent(true, false, false, false, false);
            TreeViewItemProps.SetIsCanceled(tvi, false);
            TreeViewItemProps.SetIsLoading(tvi, true);
            tvi.Background = _normalBrush;

            // If this resource does not exist on the server then create, else update
            if (((TVIState)tvi.Tag).IsRemoteExistantKnown && !((TVIState)tvi.Tag).IsRemoteExistant)
                _workMaster.AddJob(this, Master.JobType.CreateResource, ((TVIState)tvi.Tag).Resource, 
                    CreateResourceCallback, 100000);
            else
                _workMaster.AddJob(this, Master.JobType.SaveResource, ((TVIState)tvi.Tag).Resource, 
                    SaveResourceCallback, 100000);
        }

        /// <summary>
        /// Finds the specified <see cref="FullAsset"/> in the tree.
        /// </summary>
        /// <param name="fullAsset">The <see cref="FullAsset"/>.</param>
        /// <returns>A <see cref="TreeViewItem"/> if located; otherwise, <c>null</c>.</returns>
        /// <remarks>Runs on the UI thread.</remarks>
        TreeViewItem FindTreeViewItem(Resource resource)
        {
            TreeViewItem tvi;

            for (int i = 0; i < ResourceTree.Items.Count; i++)
            {
                tvi = (TreeViewItem)ResourceTree.Items[i];
                if (tvi.Tag != null) // If null, then Tag has not been set, which means that LoadResource has not been called and thus, it cannot be the one we want
                {
                    if (((TVIState)tvi.Tag).Resource.Guid == resource.Guid)
                        return tvi;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the specified <see cref="Guid"/> in the tree.
        /// </summary>
        /// <param name="guid">The <see cref="Guid"/>.</param>
        /// <returns>A <see cref="TreeViewItem"/> if located; otherwise, <c>null</c>.</returns>
        /// <remarks>Runs on the UI thread.</remarks>
        TreeViewItem FindTreeViewItem(Guid guid)
        {
            TreeViewItem tvi;

            // Locate the resource in the tree
            for (int i = 0; i < ResourceTree.Items.Count; i++)
            {
                tvi = (TreeViewItem)ResourceTree.Items[i];
                if (tvi.Tag != null) // If null, then Tag has not been set, which means that LoadResource has not been called and thus, it cannot be the one we want
                {
                    if (((TVIState)tvi.Tag).Resource.Guid == guid)
                    {
                        return tvi;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the specified <see cref="string"/> in the tree.
        /// </summary>
        /// <param name="header">A string representing the Guid in the header.</param>
        /// <returns>A <see cref="TreeViewItem"/> if located; otherwise, <c>null</c>.</returns>
        /// <remarks>Runs on the UI thread.</remarks>
        TreeViewItem FindTreeViewItem(string header)
        {
            TreeViewItem tvi;

            // Locate the resource in the tree
            for (int i = 0; i < ResourceTree.Items.Count; i++)
            {
                tvi = (TreeViewItem)ResourceTree.Items[i];
                if (tvi.Tag != null) // If null, then Tag has not been set, which means that LoadResource has not been called and thus, it cannot be the one we want
                {
                    if (tvi.Header.ToString() == header)
                    {
                        return tvi;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Handles the Click event of the BtnOpenSelected control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnOpenSelected_Click(object sender, RoutedEventArgs e)
        {
            TreeViewItem tvi;
            ResourceDelegate actOpenResource = OpenResource;

            if (ResourceTree.SelectedItem == null)
            {
                MessageBox.Show("You must select a resource first.");
                return;
            }

            tvi = (TreeViewItem)ResourceTree.SelectedItem;

            if (tvi.Tag != null)
            {
                tvi.Background = Brushes.Red;
                actOpenResource.BeginInvoke(tvi, ((TVIState)tvi.Tag).Resource, 
                    OpenResource_AsyncCallback, actOpenResource);
            }
        }

        /// <summary>
        /// Updates the status of a <see cref="TreeViewItem"/>.
        /// </summary>
        /// <param name="tvi">The <see cref="TreeViewItem"/> to update.</param>
        /// <param name="status">The new status.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void UpdateStatus(TreeViewItem tvi, string status)
        {
            TreeViewItemProps.SetStatus(tvi, status);
            if (((TVIState)tvi.Tag).Resource.Guid == _statusBarItemGuid)
                SBItem.Content = status;
        }


        #region OpenResource

        /// <summary>
        /// Opens the resource.
        /// </summary>
        /// <param name="tvi">The <see cref="TreeViewItem"/> containing the resource.</param>
        /// <param name="resource">The <see cref="Resource"/>.</param>
        /// <remarks>Runs on a background thread.</remarks>
        private void OpenResource(TreeViewItem tvi, Resource resource)
        {
            string errorMessage;
            ResourceDelegate actCloseResource = CloseResource;

            if (!ExternalApplication.OpenFileWithDefaultApplication(resource.DataAsset, out errorMessage))
                MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            Dispatcher.BeginInvoke(actCloseResource, System.Windows.Threading.DispatcherPriority.Background, tvi, resource);
        }

        /// <summary>
        /// Called when a resource is released.
        /// </summary>
        /// <param name="tvi">The <see cref="TreeViewItem"/> containing the resource.</param>
        /// <param name="resource">The <see cref="Resource"/>.</param>
        /// <remarks>
        /// Runs on the UI thread.
        /// Can be called at any point after opening, depending on how the application handles file access.
        /// </remarks>
        private void CloseResource(TreeViewItem tvi, Resource resource)
        {
            tvi.Background = Brushes.Transparent;
        }

        /// <summary>
        /// Called to end invoke on UI thread to process any exceptions, etc.
        /// </summary>
        /// <param name="iAR">The <see cref="IAsyncResult"/>.</param>
        private void OpenResource_AsyncCallback(IAsyncResult iAR)
        {
            // Call end invoke on UI thread to process any exceptions, etc.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                (Action)(() => OpenResource_EndInvoke(iAR)));
        }

        /// <summary>
        /// Called to notify the UI of end invoke.
        /// </summary>
        /// <param name="iAR">The <see cref="IAsyncResult"/>.</param>
        private void OpenResource_EndInvoke(IAsyncResult iAR)
        {
            try
            {
                var actInvoked = (ResourceDelegate)iAR.AsyncState;
                actInvoked.EndInvoke(iAR);
            }
            catch (Exception ex)
            {
                // Probably should check for useful inner exceptions
                MessageBox.Show(string.Format("Error in ProcessEndInvokeOpenResource\r\nException:  {0}",
                    ex.Message));
            }
        }

        #endregion

        /// <summary>
        /// Handles the Click event of the BtnExit control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < Application.Current.Windows.Count; i++)
                Application.Current.Windows[i].Close();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Handles the Click event of the BtnRefreshETagStatus control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnRefreshETagStatus_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateStatusJob.UpdateUIDelegate actUpdateUI = CheckUpdateStatus;
            for (int i = 0; i < ResourceTree.Items.Count; i++)
            {
                _workMaster.AddJob(this, Master.JobType.CheckUpdateStatus, 
                    ((TVIState)((TreeViewItem)ResourceTree.Items[i]).Tag).Resource, 
                    actUpdateUI, 1000);
            }
        }

        /// <summary>
        /// Handles the Click event of the BtnSearch control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            //SearchWindow search = new SearchWindow();
            //search.OnResultSelected += new SearchWindow.SearchResultHandler(search_OnResultSelected);
            //search.Show();
        }

        /// <summary>
        /// Called when a search result is selected.
        /// </summary>
        /// <param name="guid">The <see cref="Guid"/> of the selected resource.</param>
        /// <remarks>Runs on the UI thread.</remarks>
        void search_OnResultSelected(Guid guid)
        {
            //if (FindTreeViewItem(guid) != null)
            //    return;

            //GetResourceJob.UpdateUIDelegate actUpdateUI = GetResourceCallback;
            //MetaAsset ma = new MetaAsset(guid, FileSystem);
            //if(ma.ResourceExistsOnFilesystem())
            //    ma.Load();
            //FullAsset fullAsset = new FullAsset(ma, new DataAsset(ma, FileSystem));
            ////string datapath = resource.StorageLocation + new AssetType(AssetType.Data).VirtualPath + "\\";

            //_workMaster.AddJob(this, Master.JobType.LoadResource, fullAsset, actUpdateUI, 150000);
        }

        /// <summary>
        /// WorkReport accepts a UpdateUIDelegate and its associated arguments and should handle pumping this message to the UI
        /// </summary>
        /// <param name="actUpdateUI">The method to update the UI.</param>
        /// <param name="job">The job for the method updating the UI.</param>
        /// <param name="fullAsset">The <see cref="FullAsset"/> for the method updating the UI.</param>
        /// <remarks>Runs on the job's thread.</remarks>
        public void WorkReport(JobBase.UpdateUIDelegate actUpdateUI, JobBase job, Resource resource)
        {
            Dispatcher.BeginInvoke(actUpdateUI, job, resource);
        }

        /// <summary>
        /// Selects the <see cref="TreeViewItem"/> at the specified index in the tree.
        /// </summary>
        /// <param name="index">The index.</param>
        public void SetResourceTreeSelectedIndex(int index)
        {
            DependencyObject dObject = ResourceTree.ItemContainerGenerator.ContainerFromIndex(index);

            ((TreeViewItem)dObject).IsSelected = true;

            System.Reflection.MethodInfo selectMethod = typeof(TreeViewItem).GetMethod("Select", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            selectMethod.Invoke(dObject, new object[] { true });
        }

        /// <summary>
        /// TEST - This is testing code used to generate a new full asset saving both its meta and 
        /// data assets to disk and returning the instantiated FullAsset object.
        /// </summary>
        /// <returns></returns>
        private Common.Storage.Resource GenerateResource()
        {
            Common.FileSystem.IOStream iostream;
            byte[] buffer;
            Common.Storage.MetaAsset ma;
            Common.Storage.DataAsset da;
            Resource resource;
            List<string> tags = new List<string>();
            Dictionary<string, object> dict1 = new Dictionary<string,object>();
            
            tags.Add("tag1");
            tags.Add("tag2");
            tags.Add("tag3");
            dict1.Add("prop1", 1);
            dict1.Add("prop2", DateTime.Now);
            dict1.Add("prop3", "lucas");

            // Create new meta asset
            ma = Common.Storage.MetaAsset.Instantiate(Guid.NewGuid(), "lucas", DateTime.Now, 
                "Lucas", 0, null, ".txt", DateTime.Now, DateTime.Now, "Test", tags, 
                dict1, _couchdb);
            resource = new Resource(ma, _couchdb);
            da = resource.DataAsset;

            // Open the stream to create the new data asset
            iostream = FileSystem.Open(da.RelativePath, FileMode.Create, FileAccess.Write, FileShare.None,
                FileOptions.None, "WindowsClient.MainWindow.GenerateFullAsset()");

            // Write the new data asset
            buffer = System.Text.Encoding.UTF8.GetBytes("Test Document");
            iostream.Write(buffer, buffer.Length);
            FileSystem.Close(iostream);

            // Update the meta asset with md5 and length
            ma.Length = (ulong)buffer.Length;
            ma.Md5 = FileSystem.ComputeMd5(da.RelativePath);

            // Write the ma out to disk
            ma.SaveToLocal(null, FileSystem);

            return resource;
        }

        /// <summary>
        /// Handles the Click event of the Btn1 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        private void Btn1_Click(object sender, RoutedEventArgs e)
        {
            //if (ResourceTree.SelectedItem == null)
            //{
            //    MessageBox.Show("A resource must be selected.");
            //    return;
            //}

            //TVIState tviState = (TVIState)((TreeViewItem)ResourceTree.SelectedItem).Tag;
            //Common.Data.MetaAsset ma = tviState.FullAsset.MetaAsset;
            //LoadResourceJob.UpdateUIDelegate actUpdateUI = CheckETagStatus;

            //tviState.FullAsset.MetaAsset.UpdateByServer(ma.ETag.Increment(), ma.MetaVersion + 1, 
            //    ma.DataVersion + 1, ma.LockedBy, ma.LockedAt, ma.Creator, ma.Length, ma.Md5, 
            //    ma.Created, ma.Modified, ma.LastAccess);

            //ma.Save();

            //_workMaster.AddJob(this, Master.JobType.GetETag, tviState.FullAsset, actUpdateUI, (uint)Settings.Instance.NetworkTimeout);
        }

        /// <summary>
        /// Handles the Click event of the BtnSettings control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow win = new SettingsWindow();
            win.ShowDialog();
        }

        /// <summary>
        /// Handles the Click event of the Btn2 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        private void Btn2_Click(object sender, RoutedEventArgs e)
        {
            //FullAsset fullAsset;
            //LoadResourceJob.UpdateUIDelegate actUpdateUI = CheckETagStatus;

            //fullAsset = GenerateFullAsset();
            //_workMaster.AddJob(this, Master.JobType.GetETag, fullAsset, actUpdateUI, 100000);
        }

        /// <summary>
        /// Handles the Click event of the BtnAddResource control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
        private void BtnAddResource_Click(object sender, RoutedEventArgs e)
        {
            TreeViewItem tvi;
            TVIState tviState;
            Resource resource;
            string metaRelPath, dataRelPath, dataExt, rootPath;
            Guid guid = Guid.NewGuid();
            List<string> uprop = new List<string>();
            System.Windows.Forms.OpenFileDialog ofd;
            
            if(MessageBox.Show("This process will make a copy of the selected resource adding it to the repository.  Understand " +
                "that this will not affect your original file and changes made to your original file will not be made in the " + 
                "repository.  Would you like to add a resource at this time?",
                "Add a Resource", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes)
            {
                ofd = new System.Windows.Forms.OpenFileDialog();
                ofd.Multiselect = false;
                
                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    dataExt = System.IO.Path.GetExtension(ofd.FileName);
                    while(FileSystem.ResourceExists(Common.FileSystem.Path.RelativeDataPath + guid.ToString("N") + dataExt) ||
                        FileSystem.ResourceExists(Common.FileSystem.Path.RelativeMetaPath + guid.ToString("N") + ".xml"))   
                    {
                        guid = Guid.NewGuid();
                    }
                    
                    // Create Resource
                    resource = new Resource(guid, _couchdb);

                    // Copy DataAsset
                    File.Copy(ofd.FileName, resource.DataAsset.RelativePath);

                    // Fill out the MA info
                    resource.MetaAsset.Extension = dataExt;
                    resource.MetaAsset.Created = DateTime.Now;
                    resource.MetaAsset.Creator = TEMP_USERNAME;
                    resource.MetaAsset.LastAccess = DateTime.Now;
                    resource.MetaAsset.Length = FileSystem.GetFileLength(resource.DataAsset.RelativePath);
                    resource.MetaAsset.LockedAt = DateTime.Now;
                    resource.MetaAsset.LockedBy = TEMP_USERNAME;
                    resource.MetaAsset.Md5 = FileSystem.ComputeMd5(resource.DataAsset.RelativePath);

                    resource.MetaAsset.Tags.Add("Seperate tags");
                    resource.MetaAsset.Tags.Add("by a return");

                    resource.MetaAsset.UserProperties.Add("prop1", (int)1);
                    resource.MetaAsset.UserProperties.Add("prop2", DateTime.Now);
                    resource.MetaAsset.UserProperties.Add("prop3", "string1");

                    resource.MetaAsset.SaveToLocal(null, FileSystem);
                    resource.DataAsset.Save(new Common.FileSystem.MetaResource(guid, FileSystem), true);

                    tvi = AddTreeResource(resource, false, true);
                    tvi.Background = _needUpdatedBrush;
                    UpdateStatus(tvi, "File needs saved to server");

                    tviState = (TVIState)tvi.Tag;
                    tviState.UpdateResourceStatus(true, false, false, false, true);
                    tviState.UpdateEvent(false, true, false, false, false);                    
                }
            }
        }
    }
}