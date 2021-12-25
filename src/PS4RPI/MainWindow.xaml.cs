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
using System.Xml.Linq;
using Microsoft.WindowsAPICodePack.Dialogs;


//https://gist.github.com/flatz/60956f2bf1351a563f625357a45cd9c8

namespace PS4RPI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        private const string version = "v2.0-pre1";
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
        private bool patch_dlc_include = false;

        private string _loadDirectory;
        public string loadDirectory 
        {
            get { return _loadDirectory; }
            set { _loadDirectory = value; RaisePropertyChanged(); }
        }
        private bool _isbusy;
        public bool IsBusy
        {
            get { return _isbusy; }
            set { _isbusy = value; RaisePropertyChanged(); }
        }

        private PkgFile _selectedFile;
        public PkgFile SelectedFile
        {
            get { return _selectedFile; }
            set { _selectedFile = value; RaisePropertyChanged(); }
        }

        private int _progress;
        private int _progressTotal;
        public int Progress
        {
            get { return _progress; }
            set { _progress = value; RaisePropertyChanged(); }
        }
        public int ProgressTotal
        {
            get { return _progressTotal; }
            set { _progressTotal = value; RaisePropertyChanged(); }
        }
        public List<PkgFile> pkg_list = new List<PkgFile>();

        private bool launchbox_mode;

        public ObservableCollection<object> RootDirectoryItems { get; } = new ObservableCollection<object>();
        
        public MainWindow(string[] args)
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded; ;
            Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;

            argAutostart = args.Contains("/autostart");            
            
            foreach(var arg in args)
            {
                if (launchbox_mode == true) 
                {
                    if (File.Exists(arg))
                    {
                        argAutostart = true;
                        pkg_in = arg;                        
                    }
                    else
                    {
                        launchbox_mode = false;
                        patch_dlc_include = false;
                        MessageBox.Show("Missing a valid package name in LaunchBox mode");                        
                    }
                    break;
                }                
                
                if (arg.StartsWith("/lb")) launchbox_mode = true;
                if (arg.StartsWith("/lb-all"))
                {
                    launchbox_mode = true;
                    patch_dlc_include = true;
                }
            }
            
            Title += $" {version}";
        }

        private void CleanHardlinkDir(string hardlink_dir)
        {
            // Handle Install Dir
            if (!Directory.Exists(hardlink_dir))
            {
                System.IO.Directory.CreateDirectory(hardlink_dir);
            }
            else
            {
                //clean up
                System.IO.DirectoryInfo di = new DirectoryInfo(hardlink_dir);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
            }
        }


        private PkgFile GetPackageFromFile(string pkg_in, string hardlink_dir)
        {

            //get paramsfo 
            Unprotected_PKG ps4pkg = Read_PKG(pkg_in);
            Param_SFO.PARAM_SFO psfo = ps4pkg.Param;

            string title = CleanFileName(psfo.Title);
            PKGType pkgType = ps4pkg.PKG_Type;            
            string content_id = psfo.ContentID;
            string version = String.Empty;
            
            foreach (Param_SFO.PARAM_SFO.Table t in ps4pkg.Param.Tables.ToList())
            {
                if (t.Name == "VERSION")
                {
                    version = t.Value.Replace(".", ""); //convert value from string to int
                }
            }

            string hardlink = content_id + "-A" + psfo.APP_VER.Replace(".", "") + "-V" + version.Replace(".", "") + ".pkg";

            // content_id = psfo.ContentID + "-A0000-V" + version.Replace(".", "");

            return new PkgFile()
            {
                FilePath = pkg_in,
                Length = ByteSizeLib.ByteSize.FromBytes(new FileInfo(pkg_in).Length),                
                ContentId = content_id,
                Title = title,
                TitleId = psfo.TITLEID,
                Type = pkgType,
                HardLinkPath = Path.Combine(hardlink_dir, hardlink)

            };
        }

        private static String CleanFileName(string fileName)
        {
            fileName = fileName.Replace("꞉", " -").Replace("®", String.Empty).Replace("™", String.Empty).Replace(":", " -").Replace("：", " -");
            String clean = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
            return clean;
        }
  
        private void CreateHardLinks(List<PkgFile> pkgFiles)
        {
            if (pkgFiles is null)
            {
                throw new ArgumentNullException(nameof(pkgFiles));
            }

            String curDir = new FileInfo(pkgFiles[0].FilePath).DirectoryName;
            String installDir = new FileInfo(pkgFiles[0].HardLinkPath).DirectoryName;         

            // Update PS4RPI config
      /*      String configFile = Path.Combine(Directory.GetCurrentDirectory(), "PS4RPI.dll.config");
            XElement configXml = XElement.Load(configFile);
            var elm = configXml.Descendants("add").FirstOrDefault(el => el.Attribute("key").Value == "folder_or_url");
            elm.Attribute("value").Value = installDir;
            configXml.Save(configFile);*/


            // Create HardLinks
            foreach (PkgFile pkg in pkgFiles)
            {
                CreateHardLink(pkg.HardLinkPath, pkg.FilePath, IntPtr.Zero);
            }

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
                    pcfolder = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName;

                    if (!Uri.IsWellFormedUriString(pcfolder, UriKind.Absolute))
                    {
                        Application.Current.Shutdown(0);
                        return;
                    }
                    Title += $" | PC {pcip}:{pcport} | PS4 {PsIP}";
                    client = new RestClient(PsIP);
                    server = new SimpleServer(new IPAddress[] { IPAddress.Parse(pcip) }, pcport, pcfolder);
                    await server.Start(Cts.Token);
                }
                else
                {
                    Application.Current.Shutdown(0);
                    return;
                }

                //enumerate files
                await loadFileList(launchbox_mode);

            }
            catch (Exception ex)
            {
                var message = ex.Message;
                if (argAutostart)
                    message += "\n\nPlease run without /autostart argument and properly configure the app.";

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

 

        private async Task loadFileList(bool launchbox_mode)
        {
            RootDirectoryItems.Clear();

            if (launchbox_mode)
            {
                await PreparePackageFilesToTransfer();
            }
            else
            {
                await ReadPackageFilesFromDirectory();
            }
            
            
            foreach (var item in pkg_list) {
                RootDirectoryItems.Add(item);                
            }
        }

        private async Task PreparePackageFilesToTransfer()
        {
            string searchRoot = new FileInfo(pkg_in).DirectoryName;
            string hardlink_dir = Path.Combine(Directory.GetCurrentDirectory(), "[Install]");
            string[] all_packages = Directory.GetFiles(searchRoot, "*.pkg", SearchOption.AllDirectories);
            pbTransferTotal.Maximum = all_packages.Length;
            loadDirectory = "[Filtered] in " + searchRoot;

            await Task.Run(() =>
            {
                // get list of files                
                PkgFile main_pkg = GetPackageFromFile(pkg_in, hardlink_dir);
                pkg_list.Add(main_pkg);
                if (patch_dlc_include == false) return;

                if ((main_pkg.Type != PKGType.Game) && (main_pkg.Type != PKGType.Patch))
                {
                    MessageBox.Show("For /lb-all option, you need to pass either main game or patch file");
                    pkg_list.Clear();
                    return;
                }

                ProgressTotal = 0;
                foreach (var file in all_packages)
                {
                    if (file.Contains(main_pkg.TitleId))
                    {
                        if (file.Equals(pkg_in)) continue;
                        pkg_list.Add(GetPackageFromFile(file, hardlink_dir));
                    }
                    ProgressTotal++;
                }
            });
        }

        private async Task ReadPackageFilesFromDirectory()
        {
            var gameDir = new DirectoryInfo(pcfolder);
            loadDirectory = pcfolder;
            pkg_list.Clear();
            

            pbTransferTotal.Maximum = gameDir.GetFiles("*.pkg").Length;
            pbTransfer.Maximum = pbTransferTotal.Maximum;
            await Task.Run(() =>
            {
                foreach (var file in gameDir.GetFiles("*.pkg").OrderBy(x => x.Name))
                {
                    Unprotected_PKG read = Read_PKG(file.FullName);
                    Param_SFO.PARAM_SFO psfo = read.Param;

                    pkg_list.Add(new PkgFile
                    {
                        FilePath = file.FullName,
                        Length = ByteSizeLib.ByteSize.FromBytes(file.Length),
                        Type = read.PKG_Type,
                        Title = psfo.Title,
                        TitleId = psfo.TITLEID
                    });
                    ProgressTotal++;
                }
            });
        }

        //private void getSubDirectoriesAndFiles(DirectoryInfo dir, UserDirectory ud)
        //{
        //    foreach (var file in dir.GetFiles("*.pkg").OrderBy(x => x.Name))
        //    {
        //        ud.Files.Add(new UserFile
        //        {
        //            FilePath = file.FullName
        //        });
        //    }

        //    foreach (var item in dir.GetDirectories().OrderBy(x => x.Name))
        //    {
        //        var d = new UserDirectory
        //        {
        //            DirectoryPath = item.FullName,
        //        };
        //        ud.Subfolders.Add(d);
        //        getSubDirectoriesAndFiles(item, d);
        //    }
        //}



        private async void ButtonSend_Click(object sender, RoutedEventArgs e)
        {            
            IsBusy = true;
            tbStats.Text = "Start transfering...";
            
            var ins = new Models.RequestInstall();
            ins.packages = new List<string>();
            foreach (PkgFile pkg in lbPackage.SelectedItems)
            {
                var relative = Path.GetRelativePath(pcfolder, pkg.FilePath);
                var escapedRelative = string.Join('/', relative.Split(Path.DirectorySeparatorChar).Select(x => Uri.EscapeUriString(x)));
                var itemUrl = $"http://{pcip}:{pcport}/{escapedRelative}";
                ins.packages.Add(itemUrl);                
            }
            
            try{
                var result = await client.Install(ins, Cts.Token);
                if (result.status == "success")
                {
                    await Task.Delay(500);

                    tbStats.Text = $"Task id {result.task_id}\nKeep this progrem open while the console is downloading the file\n";
                    long progressError = 0;
                    Progress = 0;
                    ProgressTotal = 0;
                    try{
                        bool prep = false;
                        bool sending = false;
                        
                        while (true)
                    {
                        await Task.Delay(10000);
                        var progress = await client.GetTaskProcess(new Models.RequestTaskID { task_id = result.task_id }, Cts.Token);
                        if (progress.status == "success")
                            progressError = progress.error;
                        // tbStats.Text += $"\nProgress Status {progress.status} {progress.transferred}/{progress.length} numtotal:{progress.num_total} rest:{progress.rest_sec}";

                        if (progress.preparing_percent < 100)
                        {
                            if (prep == false)
                            {
                                tbStats.Text += "Preparing transfer....\n";
                                prep = true;
                            }

                        }
                        else
                        {
                            if (sending == false)
                            {
                                ProgressTotal = 0;
                                tbStats.Text += "Starting pkg transfer....\n";
                                sending = true;
                            }
                            Progress = (int)((float)progress.transferred / progress.length * 100);
                            ProgressTotal = (int)((float)progress.transferred_total / progress.length_total * 100);
                            if (progress.transferred_total >= progress.length_total)
                            {
                                tbStats.Text += "Transfer is completed!\n";
                                Progress = 0;
                                ProgressTotal = 0;
                                break;
                            }
                        }
                    }
                    }
                    catch (Exception ex)
                    {
                        tbStats.Text += ex.Message + "\n";
                        tbStats.Text += "Progress monitoring is stopped, but installation should continue w/o problem. Check PS4 for the progress\n";
                    }
                    if (progressError != 0)
                        tbStats.Text += $"\nTask error! Error code {progressError}";
                    }
                else
                {
                    tbStats.Text = JsonConvert.SerializeObject(result, Formatting.Indented);
                }
            }catch (Exception ex) {              
                tbStats.Text = ex.Message;
            }finally{
                IsBusy = false;
            }            
        }

        private async void ButtonOpenDirectory_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog openFileDialog = new CommonOpenFileDialog();
            
            openFileDialog.InitialDirectory = pcfolder;            
            openFileDialog.IsFolderPicker = true;
            if (openFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
               pcfolder = openFileDialog.FileName;
            }
            


            try
            {
                IsBusy = true;
                ProgressTotal = 0;
                await loadFileList(false);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }

        }

        private async void ButtonPkgInfo_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null)
                return;

            IsBusy = true;
            tbStats.Text = null;

            try
            {
                var p = await Task.Run(() =>
                {
                    using (var stream = File.OpenRead(SelectedFile.FilePath))
                        return new PkgReader(stream).ReadPkg();
                });

                var paramSfo = p.ParamSfo.ParamSfo;
                var titleid = paramSfo.Values.Where(x => x.Name == "TITLE_ID").FirstOrDefault()?.ToString();

                var sb = new StringBuilder();
                sb.AppendLine("Reading PKG info from file");
                sb.AppendLine(SelectedFile.Name);
                sb.AppendLine(paramSfo.Values.Where(x => x.Name == "TITLE").FirstOrDefault()?.ToString());
                sb.AppendLine(titleid);
                sb.AppendLine(p.Header.content_id);

                try
                {
                    var a = await client.IsExists(new Models.RequestTitleID { title_id = titleid }, Cts.Token);
                    if (a.status == "success")
                    {
                        if (a.exists)
                            sb.AppendLine($"Package is installed | {ByteSizeLib.ByteSize.FromBytes(a.size).ToBinaryString()}");
                        else
                            sb.AppendLine("Package is not installed");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Error connecting to console");

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
            MessageBox.Show($"PS4RPI {version} - by Sonik\n\nSpecial thanks to flatz and all devs who keep the scene alive", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }  

        private void ButtonSendAll_Click(object sender, RoutedEventArgs e)
        {
         
        }

        private async void ButtonCheckPS4_Click(object sender, RoutedEventArgs e)
        {

            if (pkg_list.Count == 0)
            {
                tbStats.Text = "Unable to check when no package files are loaded";
                return;
            }
            string filecheck = pkg_list[0].FilePath;
            IsBusy = true;
            tbStats.Text = null;


            try
            {
                var p = await Task.Run(() =>
                {
                    using (var stream = File.OpenRead(filecheck))
                        return new PkgReader(stream).ReadPkg();
                });

                var paramSfo = p.ParamSfo.ParamSfo;
                var titleid = paramSfo.Values.Where(x => x.Name == "TITLE_ID").FirstOrDefault()?.ToString();

                var sb = new StringBuilder();
                tbStats.Text = "Checking PS4 Remote Package Installer...";

                try
                {
                    var a = await client.IsExists(new Models.RequestTitleID { title_id = titleid }, Cts.Token);
                    if (a.status == "success")
                    {
                        
                        sb.AppendLine($"Package Installer is in good state");
                        
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.StartsWith("No connection could be made"))
                    {
                        sb.AppendLine(ex.Message);
                    }
                    else
                    {
                        sb.AppendLine("Remote Package Installer is not respoinding\nIf it's already running, close and restart!");
                    }

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
    }

    //public class PkgDirectory
    //{
    //    public ObservableCollection<PkgFile> Files { get; } = new ObservableCollection<PkgFile>();
    //    public ObservableCollection<PkgDirectory> Folders { get; } = new ObservableCollection<PkgDirectory>();
    //    public IEnumerable Items { get { return Folders?.Cast<object>().Concat(Files); } }
    //    public string DirectoryPath { get; set; }
    //    public string Name
    //    {
    //        get
    //        {
    //            var dname = Path.GetFileName(DirectoryPath);
    //            return string.IsNullOrEmpty(dname) ? DirectoryPath : dname;
    //        }
    //    }
    //}

    public class PkgFile
    {
        public string FilePath { get; set; }
        public ByteSizeLib.ByteSize Length { get; set; }
        public string Name { get { return Path.GetFileName(FilePath); } }
        public string HardLinkPath { get; set; }
        public PKGType Type { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
        public string ContentId { get; set; }        
    }
}
