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


//https://gist.github.com/flatz/60956f2bf1351a563f625357a45cd9c8

namespace PS4RPIReloaded
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        private const string version = "v1.0";
        private readonly CancellationTokenSource Cts = new CancellationTokenSource();
        private readonly bool argAutostart;
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
        private string pkg_in = String.Empty;
        private bool load_all_related = false;

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

        private bool launchbox_mode;
        private double baseLength;

        public static ObservableCollection<object> RootDirectoryItems { get; } = new ObservableCollection<object>();
        
        public MainWindow(string[] args)
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded; ;
            Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
            load_all_related = true;
            argAutostart = args.Contains("/autostart");

            foreach (var arg in args)
            {
                if (launchbox_mode)
                {
                    if (File.Exists(arg))
                    {
                        argAutostart = true;
                        pkg_in = arg;
                    }
                    else
                    {
                        launchbox_mode = false;
                        argAutostart = false;
                    }
                    break;
                }

                if (arg.ToLower().Equals("/lb"))
                {
                    launchbox_mode = true;
                }
            }
            Title += $" {version}";
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
                if (argAutostart || settings.ShowDialog().GetValueOrDefault())
                {
                    //todo validate all
                    pcip = settings.PcIp;
                    pcport = settings.PcPort;
                    PsIP = settings.Ps4Ip;
                    pcfolder = settings.Folder.Trim();
                    hardlink_dir = Path.Combine(Path.GetPathRoot(pcfolder), ".hardlinks");

                    Title += $" | PC {pcip}:{pcport}";
                    client = new RestClient(PsIP);
                    server = new SimpleServer(new IPAddress[] { IPAddress.Parse(pcip) }, pcport, hardlink_dir);
                    await server.Start(Cts.Token);
                }
                else
                {
                    Application.Current.Shutdown(0);
                    return;
                }

                //enumerate files
                await LoadFileList(launchbox_mode);

            }
            catch (Exception ex)
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
            if (server!= null)
                server.Stop(new CancellationTokenSource(2000).Token).GetAwaiter().GetResult();
            Cts.Cancel();
        }

        private void CleanHardlinkDir()
        {
            // Handle Install Dir
            if (!Directory.Exists(hardlink_dir))
            {
                Directory.CreateDirectory(hardlink_dir);
            }
            else
            {
                //clean up
                DirectoryInfo di = new DirectoryInfo(hardlink_dir);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
            }
        }

        private PkgFile GetPackageFromFile(string pkg_in)
        {

            string bgColor = "LavenderBlush";
            //get paramsfo 
            Unprotected_PKG ps4pkg = Read_PKG(pkg_in);
            Param_SFO.PARAM_SFO psfo = ps4pkg.Param;

            string title = CleanFileName(psfo.Title);
            PKGType pkgType = ps4pkg.PKG_Type;
            string content_id = psfo.ContentID;
            string version = string.Empty;

            // item already exists, then don't create
            if (pkg_list.FindIndex(a => a.FilePath.Equals(pkg_in)) >= 0) { return null; }

            // item with duplicated hardlink, get new name
            string hardlink_path = Path.Combine(hardlink_dir, content_id + ".pkg");
            if (pkg_list.FindIndex(a => a.HardLinkPath.Equals(hardlink_path)) >= 0)
            {
                string newName = GetNewFileNameForDupe(hardlink_path);
                hardlink_path = Path.Combine(hardlink_dir, newName);
            }

            if (pkgType == PKGType.Addon_Theme) { content_id = GetFullContentID(ps4pkg, psfo); }

            string shampoodName = ShampoooFileName(title, psfo.TITLEID, content_id, pkgType, psfo.APP_VER);
            if (shampoodName.Equals(new FileInfo(pkg_in).Name)) { bgColor = "Azure"; }

            return new PkgFile()
            {
                FilePath = pkg_in,
                Length = ByteSizeLib.ByteSize.FromBytes(new FileInfo(pkg_in).Length),
                ContentId = content_id,
                Title = title,
                TitleId = psfo.TITLEID,
                Type = pkgType,
                HardLinkPath = hardlink_path,
                Version = psfo.APP_VER == string.Empty ? "N/A" : psfo.APP_VER,
                ShampoodFileName = shampoodName,
                Background = bgColor
            };
        }

        private string ShampoooFileName(string title, string title_id, string content_id, PKGType pkgType, string version)
        {
            string newName = string.Empty;
            switch (pkgType)
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
            foreach (Param_SFO.PARAM_SFO.Table t in read.Param.Tables.ToList())
            {
                if (t.Name == "VERSION")
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
            if (copiedFileSet.Count == 0)
            {
                return name;
            }
            foreach (PkgFile f in copiedFileSet)
            {
                string fileName = Path.GetFileNameWithoutExtension(f.HardLinkPath);
                try
                {
                    MatchCollection mc = Regex.Matches(fileName, "-C\\((\\d+)\\)");
                    int copyVal = int.Parse(mc[0].Groups[1].Value);
                    if (suffix <= copyVal)
                    {
                        suffix = copyVal;
                        suffix++;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    if (suffix == 0) suffix = 1;
                    continue;
                }
            }

            newName = String.Format("{0}-C({1}){2}", Path.GetFileNameWithoutExtension(name), suffix, Path.GetExtension(name));
            return newName;
        }

        private static String CleanFileName(string fileName)
        {
            fileName = fileName.Replace("꞉", " -").Replace("®", String.Empty).Replace("™", String.Empty).Replace(":", " -").Replace("：", " -");
            String clean = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
            return clean;
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
         
        }

        private async Task LoadFileList(bool launchbox_mode)
        {
            
            RootDirectoryItems.Clear();

            if (launchbox_mode)
            {
                await ReadPackageFilesFromFileInput();
            }
            else
            {
                await ReadPackageFilesFromDirectory();
            }

        }
        private void SortPackageListView()
        {
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lbPackage.ItemsSource);
            view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription("Type", ListSortDirection.Ascending));
        }

        private async Task ReadPackageFilesFromFileInput()
        {
            IsBusy = true;
            string searchRoot = new FileInfo(pkg_in).DirectoryName;            
            string[] all_packages = Directory.GetFiles(searchRoot, "*.pkg", SearchOption.AllDirectories);
            pbTransferTotal.Maximum = all_packages.Length;
            loadDirectory = "[Filtered] in " + searchRoot;
            ProgressTotal = 0;
            tbStats.Text = "";
            await Task.Run(() =>
            {
                // get list of files                
                PkgFile main_pkg = GetPackageFromFile(pkg_in);
                pkg_list.Add(main_pkg);
                Dispatcher.Invoke(() => { RootDirectoryItems.Add(main_pkg); });

                if (!load_all_related) { return; }

                foreach (string file in all_packages)
                {
                    if (file.Contains(main_pkg.TitleId)) // filtered by title_id
                    {
                        // if same file exist in the list, returns null
                        PkgFile item = GetPackageFromFile(file);                        
                        if (item == null) { continue; }
                        pkg_list.Add(item);

                        Dispatcher.Invoke(() =>
                        {
                            tbStats.Text += $"Processing... {file}\n";
                            RootDirectoryItems.Add(item);
                        });

                    }
                    Dispatcher.Invoke(() =>
                    {
                        ProgressTotal++;
                    });
                }
            });
            tbStats.Text += "Reading completed\n";
            ProgressTotal = 0;
            SortPackageListView();
            IsBusy = false;
        }

        private async Task ReadPackageFilesFromDirectory()
        {
            IsBusy = true;
            DirectoryInfo gameDir = new DirectoryInfo(pcfolder);
            loadDirectory = pcfolder;
            pkg_list.Clear();
            FileInfo[] file_list = gameDir.GetFiles("*.pkg");
            pbTransferTotal.Maximum = file_list.Length;
            await ReadPackageFilesFromFileList(file_list);
            IsBusy = false;
        }

        private async Task ReadPackageFilesFromFileList(FileInfo[] file_list)
        {
            IsBusy = true;
            tbStats.Text = "";
            await Task.Run(() =>
            {
                foreach (FileInfo file in file_list)
                {
                    PkgFile item = GetPackageFromFile(file.FullName);
                    if (item == null) { continue; }
                    pkg_list.Add(item);
                    Dispatcher.Invoke(() =>
                    {
                        tbStats.Text += $"Processing... {file}\n";
                        RootDirectoryItems.Add(item);
                        ProgressTotal++;
                    });
                }
            });
            tbStats.Text += "Reading completed\n";
            ProgressTotal = 0;
            SortPackageListView();
            IsBusy = false;
        }


        private async void ButtonSend_Click(object sender, RoutedEventArgs e)
        {
            IsBusy = true;
            tbStats.Text = "Start transfering...";

            CleanHardlinkDir();
            pbTransferTotal.Maximum = GetFilesSizeSum(lbPackage.SelectedItems).Bytes;
            ProgressTotal = 0;
            baseLength = 0;

            // download by title, install order goes from game -> patch -> DLCs 
            List<PkgFile> sortedPackages = (lbPackage.SelectedItems as List<PkgFile>)
                .OrderBy(x => x.Title)
                .ThenBy(x => x.Type).ToList();

            try
            {
                foreach (PkgFile pkg in sortedPackages)
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
                    if (result.status == "success")
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
            catch (Exception ex)
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
                    while (bCont)
                    {
                        await Task.Delay(5000);
                        Models.ResponseTaskStatus progress = await client.GetTaskProcess(new Models.RequestTaskID { task_id = task_id }, Cts.Token);

                        if (progress.preparing_percent < 100)
                        {
                            continue;
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                pkg.ProgressXfer = (double)((float)progress.transferred / progress.length_total * 100);
                                ProgressTotal = baseLength + progress.transferred;
                                if (progress.transferred >= progress.length_total)
                                {
                                    tbStats.Text += "Transfer is completed!\n";
                                    baseLength += progress.length_total;
                                    bCont = false;
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    tbStats.Text += ex.Message + "\n";
                    tbStats.Text += "Progress monitoring is stopped, but installation should continue w/o problem. Check PS4 for the progress\n";
                }
            });
        }

        private async void ButtonOpenDirectory_Click(object sender, RoutedEventArgs e)
        {
 
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(pcfolder))
                    dialog.SelectedPath = pcfolder;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    pcfolder = dialog.SelectedPath;
            }

            try
            {
                IsBusy = true;
                ProgressTotal = 0;
                await LoadFileList(false);
            }
            catch (Exception ex)
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
                    using (var stream = File.OpenRead(selectedFile.FilePath))
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
                    if (a.status == "success")
                    {
                        if (a.exists)
                        {
                            sb.AppendLine($"Main package is installed | {ByteSizeLib.ByteSize.FromBytes(a.size).ToBinaryString()}");
                            sb.AppendLine("Doesn't know which DLC or Patch are installed");
                        }
                        else
                            sb.AppendLine("Package is not installed");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine(ex.Message);
                }

                tbStats.Text = sb.ToString();
            }
            catch (Exception ex)
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

            IsBusy = true;
            tbStats.Text = null;

            try
            {
                tbStats.Text = "Checking PS4 Remote Package Installer...\n";

                try
                {
                    var a = await client.IsExists(new Models.RequestTitleID { title_id = "TEST00000" }, Cts.Token);
                    if (a.status == "success")
                    {

                        tbStats.Text += "Package Installer is in good state\n";
                        
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.StartsWith("No connection could be made\n"))
                    {
                        tbStats.Text = ex.Message;
                    }
                    else
                    {
                        tbStats.Text += "Remote Package Installer is not respoinding\nIf it's already running, close and restart!";
                    }

                }
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void lbPackage_Drop(object sender, DragEventArgs e)
        {
            string[] filePaths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (filePaths != null && filePaths.Length == 1)
            {
                pkg_in = filePaths[0];
                await ReadPackageFilesFromFileInput();
            }
            else if (filePaths != null && filePaths.Length > 0)
            {
                List<FileInfo> file_list = new List<FileInfo>();
                foreach(string path in filePaths)
                {
                    file_list.Add(new FileInfo(path));
                }
                await ReadPackageFilesFromFileList(file_list.ToArray());
            }
        }

        private void lbPackage_UnloadingRow(object sender, System.Windows.Controls.DataGridRowEventArgs e)
        {
            IsBusy = true;
            if (((DataGrid)sender).SelectedItem != null || ((DataGrid)sender).CurrentItem == null)
            {                
                IsBusy = false;
                return;
            }

            RemovePkgFromList((PkgFile)e.Row.DataContext);
            IsBusy = false;
        }

        private void RemovePkgFromList(PkgFile pkg)
        {
            pkg_list.RemoveAt(pkg_list.FindIndex(a => a.HardLinkPath.Equals(pkg.HardLinkPath)));
            if (pkg_list.Count <= 0)
            {
                tbFolder.Text = string.Empty;
            }
        }

        private void ButtonClearListItems_Click(object sender, RoutedEventArgs e)
        {
            RootDirectoryItems.Clear();
            pkg_list.Clear();
            tbStats.Text = string.Empty;            
        }

        private void ButtonShampoo_Click(object sender, RoutedEventArgs e)
        {
            int index = 0;
            foreach(var pkg in pkg_list)
            {
                try
                {
                    string newFullPath = Path.Combine(Path.GetDirectoryName(pkg.FilePath), pkg.ShampoodFileName);
                    File.Move(pkg.FilePath, newFullPath);
                    pkg.Name = pkg.ShampoodFileName;
                    ((PkgFile)RootDirectoryItems[index]).FilePath = newFullPath;
                    pkg.Background = "Azure";
                }catch(Exception ex)
                {
                    tbStats.Text = ex.Message;
                }
                
            }
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {          
            
            ((TextBox) sender).ScrollToEnd();
        }
    }


    public class PkgFile : INotifyPropertyChanged, IComparable
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

    public class RootDirectoryItems : ObservableCollection<RootDirectoryItems>
    {
        // Creating the Tasks collection in this way enables data binding from XAML.
        
    }
}
