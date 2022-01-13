using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using McMaster.DotNet.Serve;
using PS4_Tools.LibOrbis.PKG;
using System.Runtime.InteropServices;
using static PS4_Tools.PKG.SceneRelated;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Collections;
using System.Windows.Data;
using System.Diagnostics;
using System.Windows.Input;
using System.Drawing;
using System.Windows.Media.Imaging;
using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.VisualBasic.FileIO;


//https://gist.github.com/flatz/60956f2bf1351a563f625357a45cd9c8

namespace PS4RPIReloaded
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow :Window, INotifyPropertyChanged
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        private const string version = "v1.0";
        private readonly CancellationTokenSource Cts = new CancellationTokenSource();
        private string pcip = null;
        private int pcport = 0;

        private string _psip = String.Empty;
        public string PsIP
        {
            get { return _psip; }
            set { _psip = value; RaisePropertyChanged(); }
        }

        private string pcfolder = null;
        private RestClient client = null;
        private SimpleServer server = null;
        private List<string> files_in = new List<string>();        

        private string _loadDirectory;
        public string loadDirectory
        {
            get { return _loadDirectory; }
            set { _loadDirectory = value; RaisePropertyChanged(); }
        }
        private string hardlink_dir = String.Empty;

        private bool _isbusy;
        public bool IsBusy
        {
            get { return _isbusy; }
            set { _isbusy = value; RaisePropertyChanged(); }
        }

        private double _progressTotal;
        public double ProgressTotal
        {
            get { return _progressTotal; }
            set { _progressTotal = value; RaisePropertyChanged(); }
        }
        public List<PkgFile> pkg_list = new List<PkgFile>();
        
        private double baseLength;
        private bool files_mode;
        private CancellationTokenSource tokenSource;
        private string staticTitle;

        public static ObservableCollection<object> RootDirectoryItems { get; } = new ObservableCollection<object>();

        public MainWindow(string[] args)
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded; ;
            Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;            

            foreach(var arg in args)
            {
                if(File.Exists(arg)) { files_in.Add(arg); }
            }

            if (files_in.Count > 0) { files_mode = true; }
            
            Title += $" {version}";
            staticTitle = Title;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IsBusy = true;

            try
            {
                var settings = new SettingsWindow() { Owner = this };

                if(settings.PcIp == null || settings.Ps4Ip == null || settings.Folder == null)
                {
                    settings.ShowDialog().GetValueOrDefault();
                }                
                //todo validate all
                pcip = settings.PcIp;
                pcport = settings.PcPort;
                PsIP = settings.Ps4Ip;
                pcfolder = settings.Folder.Trim();
                hardlink_dir = Path.Combine(Path.GetPathRoot(pcfolder), ".hardlinks");

                Title = staticTitle + $" | PC {pcip}:{pcport}";
                client = new RestClient(PsIP);
                server = new SimpleServer(new IPAddress[] { IPAddress.Parse(pcip) }, pcport, hardlink_dir);
                await server.Start(Cts.Token);
                //enumerate files
                _ = LoadFileList(files_mode);

            }
            catch(Exception ex)
            {
                var message = ex.Message;

                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(0);
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Dispatcher_ShutdownStarted(object sender, EventArgs e)
        {
            Dispatcher.ShutdownStarted -= Dispatcher_ShutdownStarted;
            if(server != null)
                server.Stop(new CancellationTokenSource(2000).Token).GetAwaiter().GetResult();
            Cts.Cancel();
        }

        private void CleanHardlinkDir()
        {
            // Handle Install Dir
            if(!Directory.Exists(hardlink_dir))
            {
                Directory.CreateDirectory(hardlink_dir);
            }
            else
            {
                //clean up
                DirectoryInfo di = new DirectoryInfo(hardlink_dir);
                foreach(FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
            }
        }

        private PkgFile GetPackageFromFile(string pkg_file)
        {

            string bgColor = "LavenderBlush";
            //get paramsfo 
            Unprotected_PKG ps4pkg = Read_PKG(pkg_file);
            Param_SFO.PARAM_SFO psfo = ps4pkg.Param;

            string title = CleanFileName(psfo.Title);
            PKGType pkgType = ps4pkg.PKG_Type;
            string content_id = psfo.ContentID;
            string version = string.Empty;

            // item already exists, then don't create
            if(pkg_list.FindIndex(a => a.FilePath.Equals(pkg_file)) >= 0) { return null; }

            // item with duplicated hardlink, get new name
            string hardlink_path = Path.Combine(hardlink_dir, content_id + ".pkg");
            if(pkg_list.FindIndex(a => a.HardLinkPath.Equals(hardlink_path)) >= 0)
            {
                string newName = GetNewFileNameForDupe(hardlink_path);
                hardlink_path = Path.Combine(hardlink_dir, newName);
            }

            if(pkgType == PKGType.Addon_Theme) { content_id = GetFullContentID(ps4pkg, psfo); }

            string shampoodName = ShampoooFileName(title, psfo.TITLEID, content_id, pkgType, psfo.APP_VER);
            if(shampoodName.Equals(new FileInfo(pkg_file).Name)) { bgColor = "Azure"; }

            return new PkgFile()
            {
                FilePath = pkg_file,
                Length = ByteSizeLib.ByteSize.FromBytes(new FileInfo(pkg_file).Length),
                ContentId = content_id,
                Title = title,
                TitleId = psfo.TITLEID,
                Type = pkgType,
                HardLinkPath = hardlink_path,
                Version = psfo.APP_VER == string.Empty ? "N/A" : psfo.APP_VER,
                ShampoodFileName = shampoodName,
                Background = bgColor,
                RegionIcon = GetIconPath(content_id)
            };
        }

        private string GetIconPath(string content_id)
        {
            char region_code = content_id[0];
            string regionIconPath = string.Empty;
            switch(content_id[0])
            {
                case 'K':
                    regionIconPath = @"\Resources\kr.png";
                    break;
                case 'E':
                    regionIconPath = @"\Resources\eu.png";
                    break;
                case 'U':
                case 'I':
                    regionIconPath = @"\Resources\us.png";
                    break;
                case 'J':
                    regionIconPath = @"\Resources\jp.png";
                    break;
                case 'H':
                    regionIconPath = @"\Resources\hk.png";
                    break;
                case 'A':
                    regionIconPath = @"\Resources\asia.png";
                    break;
                default:
                    regionIconPath = @"\Resources\puzzled.png";
                    break;
            }
            return regionIconPath;
        }

        private string ShampoooFileName(string title, string title_id, string content_id, PKGType pkgType, string version)
        {
            string newName = string.Empty;
            switch(pkgType)
            {
                case PKGType.App:
                case PKGType.Game:
                case PKGType.Patch:
                    newName = String.Format("{0} [{1}][{2}][v{3}]", title, title_id, pkgType.ToString(), version);
                    break;

                //DLC
                case PKGType.Addon_Theme:
                    newName = String.Format("{0} [{1}][{2}][{3}]", title, title_id, "DLC", content_id);
                    break;
                case PKGType.Unknown:
                default:
                    newName = String.Format("{0} [{1}][{2}][{3}]", title, title_id, "Other", content_id);
                    break;
            }
            return newName + ".pkg";
        }

        public string GetFullContentID(Unprotected_PKG read, Param_SFO.PARAM_SFO psfo)
        {
            string version = string.Empty;
            foreach(Param_SFO.PARAM_SFO.Table t in read.Param.Tables.ToList())
            {
                if(t.Name == "VERSION")
                {
                    version = t.Value.Replace(".", ""); //convert value from string to int
                }
            }
            string content_id = psfo.ContentID + "-A" + psfo.APP_VER.Replace(".", "") + "-V" + version.Replace(".", "");
            return content_id;
        }

        private string GetNewFileNameForDupe(string name)
        {
            int suffix = 0;
            string newName;
            string pattern = Path.GetFileNameWithoutExtension(name);
            List<PkgFile> copiedFileSet = pkg_list.FindAll(a => a.HardLinkPath.Contains(pattern));
            if(copiedFileSet.Count == 0)
            {
                return name;
            }
            foreach(PkgFile f in copiedFileSet)
            {
                string fileName = Path.GetFileNameWithoutExtension(f.HardLinkPath);
                try
                {
                    MatchCollection mc = Regex.Matches(fileName, "-C\\((\\d+)\\)");
                    int copyVal = int.Parse(mc[0].Groups[1].Value);
                    if(suffix <= copyVal)
                    {
                        suffix = copyVal;
                        suffix++;
                    }
                }
                catch(ArgumentOutOfRangeException)
                {
                    if(suffix == 0) suffix = 1;
                    continue;
                }
            }

            newName = String.Format("{0}-C({1}){2}", Path.GetFileNameWithoutExtension(name), suffix, Path.GetExtension(name));
            return newName;
        }

        private static String CleanFileName(string fileName)
        {
            fileName = fileName.Replace("꞉", " -").Replace("®", String.Empty).Replace("™", String.Empty).Replace(":", " -").Replace("：", " -").Replace("’", "'").Replace("`","'");
            String clean = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
            return clean;
        }

        private async Task LoadFileList(bool files_mode)
        {
            IsBusy = true;
            RootDirectoryItems.Clear();

            if(files_mode)
            {
                if(files_in.Count == 1)
                {
                    await ReadPackageFilesFromFileInput();
                }
                else
                {
                    await ReadPackageFilesFromFileList(files_in.ToArray());
                }
                
            }
            else
            {
                await ReadPackageFilesFromDirectory();
            }
            IsBusy = false;
        }

        private void SortPackageListView()
        {
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lbPackage.ItemsSource);
            view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription("Type", ListSortDirection.Ascending));
        }

        private Task ReadPackageFilesFromFileInput()
        {            
            
            // this function is for a single file process
            if (files_in.Count != 1 ) { return Task.CompletedTask; }
            
            string searchRoot = new FileInfo(files_in[0]).DirectoryName;            
            loadDirectory = "[Filtered] in " + searchRoot;
            tbStats.Text = "";

            Task task = Task.Run(() =>
            {
                PkgFile main_pkg = null;
                Dispatcher.Invoke(() =>
                {
                    main_pkg = GetPackageFromFile(files_in[0]);
                    
                    // dupe exist then main_pkg == null
                    if(main_pkg != null)
                    {
                        pkg_list.Add(main_pkg);
                        RootDirectoryItems.Add(main_pkg);

                        string[] all_packages = Directory.GetFiles(searchRoot, "*.pkg", System.IO.SearchOption.AllDirectories)
                                .Where(f => f.Contains(main_pkg.TitleId))
                                .Where(f => !f.Equals(main_pkg.FilePath)).ToArray();                    
                        
                        ProgressTotal = 0;
                        pbTransferTotal.Maximum = all_packages.Length;
                        
                        foreach(string file in all_packages)
                        {
                            try
                            {
                                PkgFile item = GetPackageFromFile(file);
                                if(item == null) { continue; }

                                pkg_list.Add(item);
                                tbStats.Text += $"Processing... {file}\n";
                                RootDirectoryItems.Add(item);
                                ProgressTotal++;
                            }
                            catch(Exception ex)
                            {
                                tbStats.Text += $"Exception raised while processing {file}: {ex.Message}\n";                                
                                ProgressTotal++;
                                continue;
                            }                       
                        }
                    }
                               
                    SortPackageListView();
                    IsBusy = false;
                    tbStats.Text += "Reading completed\n";
                    ProgressTotal = 0;
                });              
            });
            return task;
        }

        private async Task ReadPackageFilesFromDirectory()
        {
            IsBusy = true;     
            loadDirectory = pcfolder;
            pkg_list.Clear();

            string[] file_list = Directory.GetFiles(pcfolder, "*.pkg");
            pbTransferTotal.Maximum = file_list.Length;
            await ReadPackageFilesFromFileList(file_list);
            IsBusy = false;
        }

        private async Task ReadPackageFilesFromFileList(string[] file_list)
        {
            IsBusy = true;
            tbStats.Text = "";

            tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;
            bool isAborted = false;
            Task t = Task.Run(() =>
            {
                foreach(string file in file_list)
                {
                    try { 
                        PkgFile item = GetPackageFromFile(file);
                        if(item == null) { continue; }
                        pkg_list.Add(item);
                        Dispatcher.Invoke(() =>
                        {
                            tbStats.Text += $"Processing... {file}\n";
                            RootDirectoryItems.Add(item);
                            ProgressTotal++;
                        });

                    }
                    catch(Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            tbStats.Text += $"Exception raised while processing {file}: {ex.Message}\n";            
                            ProgressTotal++;
                        });
                        continue;
                    }
                if(token.IsCancellationRequested) { isAborted = true;  return; }
                }
            });
            
            await Task.WhenAny(t);

            if(isAborted) tbStats.Text += "Parsing Aborted...!\n";
            else tbStats.Text += "Folder parsing completed\n";
            ProgressTotal = 0;
            SortPackageListView();
            IsBusy = false;
                       
        }

        private async void ButtonSend_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            tbStats.Text = "Health Checking on PS4 Package Installer ";
            var isPS4RPIReady = await HealthCheckPS4RPI();
            if(!isPS4RPIReady) 
            { 
                IsBusy = false; 
                return;
            }

            tbStats.Text = "Start transfering...";

            CleanHardlinkDir();
            pbTransferTotal.Maximum = GetFilesSizeSum(lbPackage.SelectedItems).Bytes;
            ProgressTotal = 0;
            baseLength = 0;

            // download by title, install order goes from game -> patch -> DLCs 
            List<PkgFile> sortedPackages = new List<PkgFile>();
            foreach(PkgFile f in lbPackage.SelectedItems) sortedPackages.Add(f);
            sortedPackages = sortedPackages
                .OrderBy(x => x.Title)
                .ThenBy(x => x.Type).ToList();

            try
            {
                foreach(PkgFile pkg in sortedPackages)
                {

                    bool ret = CreateHardLink(pkg.HardLinkPath, pkg.FilePath, IntPtr.Zero);
                    string relative = Path.GetRelativePath(hardlink_dir, pkg.HardLinkPath);
                    string escapedRelative = string.Join('/', relative.Split(Path.DirectorySeparatorChar).Select(x => Uri.EscapeUriString(x)));
                    Models.RequestInstall ins = new Models.RequestInstall
                    {
                        packages = new List<string>() { $"http://{pcip}:{pcport}/{escapedRelative}" }
                    };

                    await Task.Delay(3000);
                    Models.ResponseTaskTitle result = await client.Install(ins, Cts.Token);
                    if(result.status == "success")
                    {
                        tbStats.Text += $"Title {pkg.Title} ({pkg.Type})[{pkg.TitleId}]: Task id {result.task_id}\nKeep this progrem & PS4 RP Installer open while downloading\n";
                        await UpdateProgress(pkg, result.task_id);
                    }
                    else
                    {
                        tbStats.Text += JsonConvert.SerializeObject(result, Formatting.Indented);
                    }
                }
            }
            catch(Exception ex)
            {
                tbStats.Text += ex.Message;
            }
            finally
            {
                IsBusy = false;
                ProgressTotal = 100;
            }
        }

        private ByteSizeLib.ByteSize GetFilesSizeSum(IList selectedItems)
        {
            ByteSizeLib.ByteSize length = ByteSizeLib.ByteSize.FromBytes(0);

            foreach(PkgFile item in selectedItems)
            {
                length = length.Add(item.Length);
            }
            return length;
        }

        private async Task UpdateProgress(PkgFile pkg, long task_id)
        {
            pkg.ProgressXfer = 0;
            await Task.Run(async () =>
            {

                try
                {
                    bool bCont = true;
                    while(bCont)
                    {
                        await Task.Delay(5000);
                        Models.ResponseTaskStatus progress = await client.GetTaskProcess(new Models.RequestTaskID { task_id = task_id }, Cts.Token);

                        if(progress.preparing_percent < 100)
                        {
                            continue;
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                pkg.ProgressXfer = (double)((float)progress.transferred / progress.length_total * 100);
                                ProgressTotal = baseLength + progress.transferred;
                                if(progress.transferred >= progress.length_total)
                                {
                                    tbStats.Text += "Transfer is completed!\n";
                                    pkg.ProgressXfer = 100;
                                    baseLength += progress.length_total;
                                    bCont = false;
                                }
                            });
                        }
                    }
                }
                catch(Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        tbStats.Text += ex.Message + "\n";
                        tbStats.Text += "Progress monitoring is stopped, but installation should continue w/o problem. Check PS4 for the progress\n";
                    });
                }
            });
        }

        private async void ButtonOpenDirectory_Click(object sender, RoutedEventArgs e) => await OpenDirectory();

        private async Task OpenDirectory()
        {

            using(CommonOpenFileDialog dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Multiselect = false;
                dialog.DefaultDirectory = pcfolder;                
                dialog.Title = "Choose a folder with ps4 package files";
                if(dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                     pcfolder = dialog.FileName;
                }
            }

            try
            {
                IsBusy = true;
                ProgressTotal = 0;
                pkg_list.Clear();
                RootDirectoryItems.Clear();
                
                await LoadFileList(false);

            }
            catch(Exception ex)
            {
                _ = MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ButtonPkgInfo_Click(object sender, RoutedEventArgs e)
        {

            IsBusy = true;
            tbStats.Text = null;

            PkgFile selectedFile = (PkgFile)lbPackage.SelectedItems[0];

            try
            {
                var p = await Task.Run(() =>
                {
                    using(var stream = File.OpenRead(selectedFile.FilePath))
                        return new PkgReader(stream).ReadPkg();
                });

                var paramSfo = p.ParamSfo.ParamSfo;
                var titleid = paramSfo.Values.Where(x => x.Name == "TITLE_ID").FirstOrDefault()?.ToString();

                var sb = new StringBuilder();
                sb.AppendLine("Reading PKG info from file");
                sb.AppendLine(paramSfo.Values.Where(x => x.Name == "TITLE").FirstOrDefault()?.ToString() + "[" + titleid + "][" + p.Header.content_id + "]");

                try
                {
                    var a = await client.IsExists(new Models.RequestTitleID { title_id = titleid }, Cts.Token);
                    if(a.status == "success")
                    {
                        if(a.exists)
                        {
                            sb.AppendLine($"Main package is installed | {ByteSizeLib.ByteSize.FromBytes(a.size).ToBinaryString()}");
                            sb.AppendLine("Doesn't know which DLC or Patch are installed");
                        }
                        else
                            sb.AppendLine("Package is not installed");
                    }
                }
                catch(Exception ex)
                {
                    sb.AppendLine(ex.Message);
                }

                tbStats.Text = sb.ToString();
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"PS4RPI Reloaded {version} - by jayjun911\n\nSpecial thanks to Sonik(original PS4RPI) flatz and all devs who keep the scene alive", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private async void ButtonCheckPS4_Click(object sender, RoutedEventArgs e)
        {            
            await HealthCheckPS4RPI();
        }

        private async Task<bool> HealthCheckPS4RPI()
        {
            IsBusy = true;
            tbStats.Text = null;
            try
            {
                tbStats.Text = "Checking PS4 Remote Package Installer...\n";

                try
                {
                    var a = await client.IsExists(new Models.RequestTitleID { title_id = "TEST00000" }, Cts.Token);
                    if(a.status == "success")
                    {

                        tbStats.Text += "Package Installer is in good state\n";
                        IsBusy = false;
                        return true;
                    }
                }
                catch(Exception ex)
                {
                    if(ex.Message.StartsWith("No connection could be made"))
                    {
                        tbStats.Text = ex.Message + "\nmaybe incorrect ip?";
                    }
                    else
                    {
                        tbStats.Text += "Remote Package Installer is not respoinding\nIf it's already running, close it first, then restart!";
                        IsBusy = false;
                        return false;
                    }

                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);                
            }
            finally
            {
                IsBusy = false;
            }
            return false;
        }

        private async void lbPackage_Drop(object sender, DragEventArgs e)
        {
            IsBusy = true;
            string[] filePaths = e.Data.GetData(DataFormats.FileDrop) as string[];
            files_in.Clear();
            files_in.AddRange(filePaths);
             
            if(files_in.Count == 1)
            { await ReadPackageFilesFromFileInput();}
            else { await Task.FromResult(ReadPackageFilesFromFileList(files_in.ToArray())); }
    
            tbFolder.Text = "[Filtered]";
            IsBusy = false;
            
        }

        private void RemovePkgFromList(PkgFile pkg)
        {
            IsBusy = true;
            pkg_list.RemoveAt(pkg_list.FindIndex(a => a.HardLinkPath.Equals(pkg.HardLinkPath)));
            if(pkg_list.Count <= 0)
            {
                tbFolder.Text = string.Empty;
            }
            IsBusy = false;
        }

        private void ButtonClearListItems_Click(object sender, RoutedEventArgs e)
        {
            RootDirectoryItems.Clear();
            pkg_list.Clear();
            tbStats.Text = "All items are cleared";
            tbFolder.Text = string.Empty;
        }

        private void ButtonShampoo_Click(object sender, RoutedEventArgs e)
        {
            int index = 0;
            for(int i = 0; i < pkg_list.Count; i++)
            {
                PkgFile pkg = pkg_list[i];
                try
                {
                    string newFullPath = Path.Combine(Path.GetDirectoryName(pkg.FilePath), pkg.ShampoodFileName);
                    File.Move(pkg.FilePath, newFullPath);
                    pkg.Name = pkg.ShampoodFileName;
                    ((PkgFile)RootDirectoryItems[index]).FilePath = newFullPath;
                    pkg.Background = "Azure";
                }
                catch(Exception ex)
                {
                    tbStats.Text = ex.Message;
                }

            }
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            if(!tbFolder.Text.Equals(pcfolder))
            {
                OpenDirectory();
            }
            else
            {
                pkg_list.Clear();
                RootDirectoryItems.Clear();
                _ = ReadPackageFilesFromDirectory();
            }
        }

        private void lbPackage_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LookUpSerialStation((PkgFile)((DataGrid)sender).CurrentItem);
        }

        private void lbPackage_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if(Keyboard.IsKeyDown(Key.LeftCtrl) && e.Key == Key.Delete)
            {
                System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Delete items from file system?", "Delete?", System.Windows.Forms.MessageBoxButtons.YesNo);

                if(dialogResult == System.Windows.Forms.DialogResult.Yes)
                {
                    List<PkgFile> p_list = new List<PkgFile>();
                    foreach(var f in ((DataGrid)sender).SelectedItems) { p_list.Add((PkgFile)f); }
                    for(int i = 0; i < p_list.Count; i++)
                    {
                        try
                        {
                            FileSystem.DeleteFile(p_list[i].FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            RemovePkgFromList(p_list[i]);
                            RootDirectoryItems.Remove(p_list[i]);
                        }
                        catch(Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                }
            }
            else if(e.Key == Key.Delete)
            {
                // need to create mirror list to avoid exception as selected items gets deleted. 
                List<PkgFile> p_list = new List<PkgFile>();
                foreach(var f in ((DataGrid)sender).SelectedItems) { p_list.Add((PkgFile)f); }
                for(int i = 0; i < p_list.Count; i++)
                {
                    RemovePkgFromList(p_list[i]);
                    RootDirectoryItems.Remove(p_list[i]);
                }
            }
        }

        private void lbPackage_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if(Keyboard.IsKeyDown(Key.Enter))
            {
                LookUpSerialStation((PkgFile)((DataGrid)sender).CurrentItem);
            }
        }

        private void LookUpSerialStation(PkgFile f)
        {
            string serialStation = "https://serialstation.com/titles/";
            if(f != null)
            {
                serialStation += f.TitleId.Replace("CUSA", @"CUSA/");
                Process myProcess = new Process();
                myProcess.StartInfo.UseShellExecute = true;
                myProcess.StartInfo.FileName = serialStation;
                myProcess.Start();
            }
        }


        public class PkgFile :INotifyPropertyChanged, IComparable
        {
            public string FilePath { get; set; }
            public string ShampoodFileName { get; set; }
            public ByteSizeLib.ByteSize Length { get; set; }
            public string Name { get { return Path.GetFileName(FilePath); } set { FilePath = Path.Combine(Path.GetDirectoryName(FilePath), value); RaisePropertyChanged(); } }
            public string HardLinkPath { get; set; }
            public PKGType Type { get; set; }
            public string Title { get; set; }
            public string TitleId { get; set; }
            public string ContentId { get; set; }
            private double _progress;
            private string _regionIcon;
            public string RegionIcon
            {
                get { return _regionIcon; } 
                set { _regionIcon = value;  RaisePropertyChanged(); } 
            }
            public double ProgressXfer
            {
                get { return _progress; }
                set { _progress = value; RaisePropertyChanged(); }
            }

            private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public int CompareTo(PKGType obj)
            {
                return Type.CompareTo(obj);
            }

            int IComparable.CompareTo(object obj)
            {
                throw new NotImplementedException();
            }

            public event PropertyChangedEventHandler PropertyChanged;
            public string Version { get; set; }
            private string _background;
            public string Background
            {
                get
                {
                    return _background;
                }

                set
                {
                    _background = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(Keyboard.IsKeyDown(Key.Escape)) tokenSource.Cancel();
        }

        private void ButtonSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow() { Owner = this };
            if(settings.ShowDialog().GetValueOrDefault())
            {
                bool isChanged = false;

                if(!PsIP.Equals(settings.Ps4Ip))
                {
                    PsIP = settings.Ps4Ip;
                    client = new RestClient(PsIP);
                }
                if(!pcfolder.Equals(settings.Folder.Trim()))
                {
                    pcfolder = settings.Folder.Trim();
                    hardlink_dir = Path.Combine(Path.GetPathRoot(pcfolder), ".hardlinks");
                    files_mode = false;
                    _ = LoadFileList(files_mode);
                }
                if(!pcip.Equals(settings.PcIp) || !pcport.Equals(settings.PcPort))
                {
                    RestartServer(settings);
                }
            }
        }

        private async void RestartServer(SettingsWindow settings)
        {
            if(server != null)
                server.Stop(new CancellationTokenSource(2000).Token).GetAwaiter().GetResult();
            Cts.Cancel();
            Thread.Sleep(1000);
            pcip = settings.PcIp;
            pcport = settings.PcPort;
            Title = staticTitle + $" | PC {pcip}:{pcport}";
            server = new SimpleServer(new IPAddress[] { IPAddress.Parse(pcip) }, pcport, hardlink_dir);
            await server.Start(Cts.Token);
        }

        private void ButtonSelectAll(object sender, RoutedEventArgs e)
        {

            if(lbPackage.SelectedItems.Count < lbPackage.Items.Count)
            {
                lbPackage.SelectAll();
               
            }
            else
            {
                lbPackage.UnselectAll();
            }
        }
    }
}
