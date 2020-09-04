//
// Copyright (c) Roland Pihlakas 2019 - 2020
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using myoddweb.directorywatcher;
using myoddweb.directorywatcher.interfaces;

namespace FolderSync
{
#pragma warning disable S2223   //Warning	S2223	Change the visibility of 'xxx' or make it 'const' or 'readonly'.
    internal static class Global
    {
        public static IConfigurationRoot Configuration;


        public static string SrcPath = "";


        public static bool EnableMirror = true;
        public static bool BidirectionalMirror = false;

        public static string MirrorWatchedExtension = "*";

        public static List<string> MirrorExcludedExtensions = new List<string>() { "*~", "tmp" };
        public static List<string> MirrorIgnorePathsStartingWith = new List<string>();
        public static List<string> MirrorIgnorePathsContaining = new List<string>();

        public static string MirrorDestPath = "";



        public static bool EnableHistory = false;
        public static string HistoryWatchedExtension = "*";

        public static string HistoryVersionFormat = "PREFIX_TIMESTAMP";
        public static string HistoryVersionSeparator = "$";

        public static List<string> HistoryExcludedExtensions = new List<string>() { "*~", "tmp" };
        public static List<string> HistoryIgnorePathsStartingWith = new List<string>();
        public static List<string> HistoryIgnorePathsContaining = new List<string>();

        public static string HistoryDestPath = "";



        internal static readonly AsyncLockQueueDictionary FileOperationLocks = new AsyncLockQueueDictionary();
    }
#pragma warning restore S2223

    internal class Program
    {
        private class DummyFileSystemEvent : IFileSystemEvent
        {
            public DummyFileSystemEvent(FileSystemInfo fileSystemInfo)
            {
                FileSystemInfo = fileSystemInfo;
                FullName = fileSystemInfo.FullName;
                Name = fileSystemInfo.Name;
                Action = EventAction.Added;
                Error = EventError.None;
                DateTimeUtc = DateTime.UtcNow;
                IsFile = true;
            }

            public FileSystemInfo FileSystemInfo { get; }
            public string FullName { get; }
            public string Name { get; }
            public EventAction Action { get; }
            public EventError Error { get; }
            public DateTime DateTimeUtc { get; }
            public bool IsFile { get; }

            public bool Is(EventAction action)
            {
                return action == Action;
            }
        }

        private static byte[] GetHash(string inputString)
        {
#pragma warning disable SCS0006     //Warning	SCS0006	Weak hashing function
            HashAlgorithm algorithm = MD5.Create();
#pragma warning restore SCS0006
            return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

        public static string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }

        private static void Main()
        {
            //var environmentName = Environment.GetEnvironmentVariable("Hosting:Environment");

            var configBuilder = new ConfigurationBuilder()
                //.SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                //.AddJsonFile($"appsettings.{environmentName}.json", true)
                //.AddEnvironmentVariables()
                ;

            var config = configBuilder.Build();
            Global.Configuration = config;


            var fileConfig = config.GetSection("Files");

            Global.SrcPath = fileConfig["SrcPath"];



            Global.EnableMirror = fileConfig["EnableMirror"]?.ToUpperInvariant() != "FALSE";   //default is true
            Global.BidirectionalMirror = Global.EnableMirror && fileConfig["Bidirectional"]?.ToUpperInvariant() == "TRUE";   //default is false

            Global.MirrorDestPath = fileConfig["MirrorDestPath"] ?? fileConfig["DestPath"];

            Global.MirrorWatchedExtension = fileConfig["MirrorWatchedExtension"] ?? fileConfig["WatchedExtension"] ?? "*";

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            //Global.ExcludedExtensions = fileConfig.GetSection("ExcludedExtensions").Get<string[]>();
            Global.MirrorExcludedExtensions = fileConfig.GetSection("MirrorExcludedExtensions").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()

            Global.MirrorIgnorePathsStartingWith = fileConfig.GetSection("MirrorIgnorePathsStartingWith").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()
            Global.MirrorIgnorePathsContaining = fileConfig.GetSection("MirrorIgnorePathsContaining").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()



            Global.EnableHistory = fileConfig["EnableHistory"]?.ToUpperInvariant() == "TRUE";   //default is false

            Global.HistoryDestPath = fileConfig["HistoryDestPath"];

            Global.HistoryWatchedExtension = fileConfig["HistoryWatchedExtension"] ?? "*";

            Global.HistoryVersionFormat = fileConfig["HistoryVersionFormat"]?.ToUpperInvariant() ?? "TIMESTAMP_BEFORE_EXT";
            Global.HistoryVersionSeparator = fileConfig["HistoryVersionSeparator"] ?? ".";

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            //Global.ExcludedExtensions = fileConfig.GetSection("ExcludedExtensions").Get<string[]>();
            Global.HistoryExcludedExtensions = fileConfig.GetSection("HistoryExcludedExtensions").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()

            Global.HistoryIgnorePathsStartingWith = fileConfig.GetSection("HistoryIgnorePathsStartingWith").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()
            Global.HistoryIgnorePathsContaining = fileConfig.GetSection("HistoryIgnorePathsContaining").GetChildren().Select(c => c.Value.ToUpperInvariant()).ToList();   //NB! .ToUpperInvariant()



            var pathHashes = "";
            //TODO!!! allow multiple instances with differet settings
            pathHashes += "_" + GetHashString(Global.SrcPath.ToUpperInvariant());
            pathHashes += "_" + GetHashString(Global.MirrorDestPath?.ToUpperInvariant() ?? "");
            pathHashes += "_" + GetHashString(Global.HistoryDestPath?.ToUpperInvariant() ?? "");

            //NB! prevent multiple instances from starting on same directories
            using (Mutex mutex = new Mutex(false, "Global\\FolderSync_" + pathHashes))
            {
                if (!mutex.WaitOne(0, false))
                {
                    Console.WriteLine("Instance already running");
                }
                else
                {
                    MainTask().Wait();
                }
            }
        }

        private static async Task MainTask()
        {
            try
            {
                //Console.WriteLine(Environment.Is64BitProcess ? "x64 version" : "x86 version");
                Console.WriteLine("Press Ctrl+C to stop the monitors.");

                //start the monitor.
                using (var watch = new Watcher())
                {
                    //var drvs = System.IO.DriveInfo.GetDrives();
                    //foreach (var drv in drvs)
                    //{
                    //    if (drv.DriveType == System.IO.DriveType.Fixed)
                    //    {
                    //        watch.Add(new Request(drv.Name, true));
                    //    }
                    //}

                    watch.Add(new Request(Global.SrcPath, recursive: true));

                    if (Global.BidirectionalMirror)
                    {
                        watch.Add(new Request(Global.MirrorDestPath, recursive: true));
                    }


                    //prepare the console watcher so we can output pretty messages.
                    var consoleWatch = new ConsoleWatch(watch);


                    //start watching
                    //NB! start watching before synchronisation
                    watch.Start();


                    if (true)
                    {
                        var initialSyncMessageContext = new Context(
                            eventObj: null,
                            token: new CancellationToken(),
                            forHistory: false
                        );


                        await ConsoleWatch.AddMessage(ConsoleColor.White, "Doing initial synchronisation...", initialSyncMessageContext);

                        ConsoleWatch.DoingInitialSync = true;   //NB!

                        //1. Do initial synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
                        if (Global.EnableMirror)
                        {
                            foreach (var fileInfo in new DirectoryInfo(Global.SrcPath)
                                                        .GetFiles("*." + Global.MirrorWatchedExtension, SearchOption.AllDirectories))
                            {
                                await ConsoleWatch.OnAddedAsync
                                (
                                    new DummyFileSystemEvent(fileInfo),
                                    new CancellationToken()
                                );
                            }
                        }

                        if (
                            Global.EnableHistory 
                            && Global.MirrorWatchedExtension.ToUpperInvariant() != Global.HistoryWatchedExtension.ToUpperInvariant()    //NB!
                        )
                        {
                            foreach (var fileInfo in new DirectoryInfo(Global.SrcPath)
                                                    .GetFiles("*." + Global.HistoryWatchedExtension, SearchOption.AllDirectories))
                            {
                                await ConsoleWatch.OnAddedAsync
                                (
                                    new DummyFileSystemEvent(fileInfo),
                                    new CancellationToken()
                                );
                            }
                        }

                        if (Global.BidirectionalMirror)
                        {
                            //2. Do initial synchronisation from dest to src folder   //TODO: config for enabling and ordering of this operation
                            foreach (var fileInfo in new DirectoryInfo(Global.MirrorDestPath)
                                                    .GetFiles("*." + Global.MirrorWatchedExtension, SearchOption.AllDirectories))
                            {
                                await ConsoleWatch.OnAddedAsync
                                (
                                    new DummyFileSystemEvent(fileInfo),
                                    new CancellationToken()
                                );
                            }
                        }


                        ConsoleWatch.DoingInitialSync = false;   //NB!
                        await ConsoleWatch.AddMessage(ConsoleColor.White, "Done initial synchronisation...", initialSyncMessageContext);
                    }


                    //listen for the Ctrl+C 
                    WaitForCtrlC();

                    Console.WriteLine("Stopping...");

                    //stop everything.
                    watch.Stop();

                    Console.WriteLine("Exiting...");

                    GC.KeepAlive(consoleWatch);
                }
            }
            catch (Exception ex)
            {
                WriteException(ex);
            }
        }

        private static void WriteException(Exception ex)
        {
            if (ex is AggregateException aggex)
            {
                WriteException(aggex.InnerException);
                foreach (var aggexInner in aggex.InnerExceptions)
                {
                    WriteException(aggexInner);
                }
                return;
            }

            Console.WriteLine(ex.Message);
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                Console.WriteLine(ex.Message);
            }
        }

        private static void WaitForCtrlC()
        {
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                Console.WriteLine("Stop detected.");
                exitEvent.Set();
            };
            exitEvent.WaitOne();
        }
    }

    internal class Context
    {
        public IFileSystemEvent Event;
        public CancellationToken Token;
        public bool ForHistory;

        public DateTime Time
        {
            get
            {
                return Event?.DateTimeUtc ?? DateTime.UtcNow;
            }
        }

        public Context(IFileSystemEvent eventObj, CancellationToken token, bool forHistory)
        {
            Event = eventObj;
            Token = token;
            ForHistory = forHistory;
        }
    }

    internal class ConsoleWatch
    {
        /// <summary>
        /// The original console color
        /// </summary>
        private static readonly ConsoleColor _consoleColor = Console.ForegroundColor;

        /// <summary>
        /// We need a static lock so it is shared by all.
        /// </summary>
        private static readonly object Lock = new object();
        //private static readonly AsyncLock AsyncLock = new AsyncLock();  //TODO: use this

#pragma warning disable S2223   //Warning	S2223	Change the visibility of 'DoingInitialSync' or make it 'const' or 'readonly'.
        public static bool DoingInitialSync = false;
#pragma warning restore S2223

        private static ConcurrentDictionary<string, DateTime> BidirectionalSynchroniserSavedFileDates = new ConcurrentDictionary<string, DateTime>();
        private static readonly AsyncLockQueueDictionary FileEventLocks = new AsyncLockQueueDictionary();


        public ConsoleWatch(IWatcher3 watch)
        {
            //_consoleColor = Console.ForegroundColor;

            //watch.OnErrorAsync += OnErrorAsync;
            watch.OnAddedAsync += OnAddedAsync;
            watch.OnRemovedAsync += OnRemovedAsync;
            watch.OnRenamedAsync += OnRenamedAsync;
            watch.OnTouchedAsync += OnTouchedAsync;
        }

        public static async Task WriteException(Exception ex, Context context)
        {
            //if (ConsoleWatch.DoingInitialSync)  //TODO: config
            //    return;


            if (ex is AggregateException aggex)
            {
                await WriteException(aggex.InnerException, context);
                foreach (var aggexInner in aggex.InnerExceptions)
                {
                    await WriteException(aggexInner, context);
                }
                return;
            }

            //Console.WriteLine(ex.Message);
            StringBuilder message = new StringBuilder(ex.Message);
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                //Console.WriteLine(ex.Message);
                message.Append(Environment.NewLine + ex.Message);
            }

            await AddMessage(ConsoleColor.Red, message.ToString(), context);
        }

        public static string GetNonFullName(string fullName)
        {
            if (fullName.ToUpperInvariant().StartsWith(Global.MirrorDestPath.ToUpperInvariant()))
            {
                return fullName.Substring(Global.MirrorDestPath.Length);
            }
            else if (fullName.ToUpperInvariant().StartsWith(Global.SrcPath.ToUpperInvariant()))
            {
                return fullName.Substring(Global.SrcPath.Length);
            }
            else
            {
                throw new ArgumentException("Unexpected path provided to GetNonFullName()");
            }
        }

        public static string GetOtherFullName(string fullName, bool forHistory)
        {
            var nonFullName = GetNonFullName(fullName);

            if (forHistory)
            {
                if (fullName.ToUpperInvariant().StartsWith(Global.SrcPath.ToUpperInvariant()))
                {
                    var srcFileDate = GetFileTime(fullName);

                    if (Global.HistoryVersionFormat == "PREFIX_TIMESTAMP")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileName = Path.GetFileName(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{srcFileDate.Ticks}{Global.HistoryVersionSeparator}{fileName}");
                    }
                    else if (Global.HistoryVersionFormat == "TIMESTAMP_BEFORE_EXT")
                    {
                        var nonFullNameFolder = Path.GetDirectoryName(nonFullName);
                        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(nonFullName);
                        var fileExtension = Path.GetExtension(nonFullName);

                        return Path.Combine(Global.HistoryDestPath, nonFullNameFolder, $"{fileNameWithoutExtension}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}{fileExtension}");
                    }
                    else if (Global.HistoryVersionFormat == "SUFIX_TIMESTAMP")
                    {
                        return Path.Combine(Global.HistoryDestPath, $"{nonFullName}{Global.HistoryVersionSeparator}{srcFileDate.Ticks}");
                    }
                    else
                    {
                        throw new ArgumentException("Unexpected HistoryFileNameFormat configuration");
                    }
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
            else
            {
                if (fullName.ToUpperInvariant().StartsWith(Global.MirrorDestPath.ToUpperInvariant()))
                {
                    return Path.Combine(Global.SrcPath, nonFullName);
                }
                else if (fullName.ToUpperInvariant().StartsWith(Global.SrcPath.ToUpperInvariant()))
                {
                    return Path.Combine(Global.MirrorDestPath, nonFullName);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherFullName()");
                }
            }
        }   //public static string GetOtherFullName(string fullName, bool forHistory)

        public static async Task DeleteFile(string fullName, Context context)
        {
            try
            {                
                while (true)
                {
                    context.Token.ThrowIfCancellationRequested();

                    try
                    {
                        if (File.Exists(fullName + "~"))
                        {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                            File.Delete(fullName + "~");
#pragma warning restore SEC0116
                        }

                        if (File.Exists(fullName))
                        {
                            File.Move(fullName, fullName + "~");
                        }

                        return;
                    }
                    catch (IOException)
                    {
                        //retry after delay
#if !NOASYNC
                        await Task.Delay(1000, context.Token);     //TODO: config file?
#else
                        context.Token.WaitHandle.WaitOne(1000);
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                await WriteException(ex, context);
            }
        }   //public static async Task DeleteFile(string fullName, Context context)

        public static DateTime GetBidirectionalSynchroniserSaveDate(string fullName)
        {
            DateTime converterSaveDate;
            if (!BidirectionalSynchroniserSavedFileDates.TryGetValue(fullName, out converterSaveDate))
            {
                converterSaveDate = DateTime.MinValue;
            }

            return converterSaveDate;
        }

        public static bool NeedsUpdate(string fullName, bool forHistory)
        {
            if (DoingInitialSync)
                return true;

            var synchroniserSaveDate = GetBidirectionalSynchroniserSaveDate(fullName);
            var fileTime = GetFileTime(fullName);

            if (
                (!forHistory && !Global.BidirectionalMirror)   //no need to debounce BIDIRECTIONAL file save events when bidirectional save is disabled 
                || forHistory
                || fileTime > synchroniserSaveDate.AddSeconds(3)     //NB! ignore if the file changed during 3 seconds after bidirectional save   //TODO!! config
            )
            {
                var otherFullName = GetOtherFullName(fullName, forHistory);
                if (fileTime > GetFileTime(otherFullName))     //NB!
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task FileUpdated(string fullName, Context context)
        {
            if (
                IsWatchedFile(fullName, context.ForHistory)
                && NeedsUpdate(fullName, context.ForHistory)     //NB!
            )
            {
                var otherFullName = GetOtherFullName(fullName, context.ForHistory);
                using (await Global.FileOperationLocks.LockAsync(fullName, otherFullName, context.Token))
                {
                    //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7
                    var fileData = await FileExtensions.ReadAllBytesAsync(@"\\?\" + fullName, context.Token);
                    var originalData = fileData;

                    //save without transformations
                    await ConsoleWatch.SaveFileModifications(fullName, fileData, originalData, context);
                }
            }
        }

        private static async Task FileDeleted(string fullName, Context context)
        {
            if (IsWatchedFile(fullName, context.ForHistory))
            {
                if (!File.Exists(fullName))  //NB! verify that the file is still deleted
                {
                    var otherFullName = GetOtherFullName(fullName, context.ForHistory);

                    await DeleteFile(otherFullName, context);
                }
                else    //NB! file appears to be recreated
                {
                    await FileUpdated(fullName, context);
                }
            }
        }

        private static DateTime GetFileTime(string fullName)
        {
            try
            {
                if (File.Exists(fullName))
                    return File.GetLastWriteTimeUtc(fullName);
                else
                    return DateTime.MinValue;
            }
            catch (FileNotFoundException)    //the file might have been deleted in the meanwhile
            {
                return DateTime.MinValue;
            }
        }

        private static bool IsWatchedFile(string fullName, bool forHistory)
        {
            var fullNameInvariant = fullName.ToUpperInvariant();

            if (
                !forHistory
                &&
                (
                    fullNameInvariant.EndsWith("." + Global.MirrorWatchedExtension.ToUpperInvariant())
                    || Global.MirrorWatchedExtension == "*"
                )
                &&
                Global.MirrorExcludedExtensions.All(x =>

                    !fullNameInvariant.EndsWith("." + x)
                    &&
                    (   //handle exclusion patterns in the forms like *xyz
                        !x.StartsWith("*")
                        || fullNameInvariant.Length < x.Length - 1
                        || !fullNameInvariant.EndsWith(/*"." + */x.Substring(1))    //NB! the existence of dot is not verified in this case     //TODO: use Regex
                    )
                )
            )
            {
                var nonFullNameInvariant = GetNonFullName(fullNameInvariant);

                if (
                    Global.MirrorIgnorePathsStartingWith.Any(x => nonFullNameInvariant.StartsWith(x))
                    || Global.MirrorIgnorePathsContaining.Any(x => nonFullNameInvariant.Contains(x))
                )
                {
                    return false;
                }

                return true;
            }
            else if (
                forHistory
                &&
                (
                    fullNameInvariant.EndsWith("." + Global.HistoryWatchedExtension.ToUpperInvariant())
                    || Global.HistoryWatchedExtension == "*"
                )
                &&
                Global.HistoryExcludedExtensions.All(x =>

                    !fullNameInvariant.EndsWith("." + x)
                    &&
                    (   //handle exclusion patterns in the forms like *xyz
                        !x.StartsWith("*")
                        || fullNameInvariant.Length < x.Length - 1
                        || !fullNameInvariant.EndsWith(/*"." + */x.Substring(1))    //NB! the existence of dot is not verified in this case     //TODO: use Regex
                    )
                )
            )
            {
                var nonFullNameInvariant = GetNonFullName(fullNameInvariant);

                if (
                    Global.HistoryIgnorePathsStartingWith.Any(x => nonFullNameInvariant.StartsWith(x))
                    || Global.HistoryIgnorePathsContaining.Any(x => nonFullNameInvariant.Contains(x))
                )
                {
                    return false;
                }

                return true;
            }

            return false;

        }   //private bool IsWatchedFile(string fullName, bool forHistory)

#pragma warning disable AsyncFixer01
        private static async Task OnRenamedAsync(IRenamedFileSystemEvent fse, CancellationToken token)
        {
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false),
                new Context(fse, token, forHistory: true)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.PreviousFileSystemInfo.FullName, context.ForHistory)
                            || IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            await AddMessage(ConsoleColor.Cyan, $"[{(fse.IsFile ? "F" : "D")}][R]:{fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", context);

                            //NB! if file is renamed to cs~ or resx~ then that means there will be yet another write to same file, so lets skip this event here
                            if (!fse.FileSystemInfo.FullName.EndsWith("~"))
                            {
                                //using (await Global.FileOperationLocks.LockAsync(rfse.FileSystemInfo.FullName, rfse.PreviousFileSystemInfo.FullName, context.Token))  //comment-out: prevent deadlock
                                {
                                    await FileUpdated(fse.FileSystemInfo.FullName, context);
                                    await FileDeleted(fse.PreviousFileSystemInfo.FullName, context);
                                }
                            }
                        }
                    }
                    else
                    {
                        await AddMessage(ConsoleColor.Cyan, $"[{(fse.IsFile ? "F" : "D")}][R]:{fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", context);

                        //TODO trigger update / delete event for all files in new folder
                    }
                }
                catch (Exception ex)
                {
                    await WriteException(ex, context);
                }
            }
        }   //private static async Task OnRenamedAsync(IRenamedFileSystemEvent fse, CancellationToken token)

        private static async Task OnRemovedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false),
                new Context(fse, token, forHistory: true)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            await AddMessage(ConsoleColor.Yellow, $"[{(fse.IsFile ? "F" : "D")}][-]:{fse.FileSystemInfo.FullName}", context);

                            using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                            {
                                await FileDeleted(fse.FileSystemInfo.FullName, context);
                            }
                        }
                    }
                    else
                    {
                        //nothing to do here: the files are likely already deleted by now
                    }
                }
                catch (Exception ex)
                {
                    await WriteException(ex, context);
                }
            }
        }

        public static async Task OnAddedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false),
                new Context(fse, token, forHistory: true)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            //await AddMessage(ConsoleColor.Green, $"[{(fse.IsFile ? "F" : "D")}][+]:{fse.FileSystemInfo.FullName}", context);

                            using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                            {
                                await FileUpdated(fse.FileSystemInfo.FullName, context);
                            }
                        }
                    }
                    else
                    {
                        //nothing to do here: there are likely no files in here yet
                    }
                }
                catch (Exception ex)
                {
                    await WriteException(ex, context);
                }
            }
        }

        private static async Task OnTouchedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false),
                new Context(fse, token, forHistory: true)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (
                        fse.IsFile
                        && File.Exists(fse.FileSystemInfo.FullName)     //for some reason fse.IsFile is set even for folders
                    )
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            await AddMessage(ConsoleColor.Gray, $"[{(fse.IsFile ? "F" : "D")}][T]:{fse.FileSystemInfo.FullName}", context);

                            using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                            {
                                await FileUpdated(fse.FileSystemInfo.FullName, context);
                            }
                        }
                    }
                    else
                    {
                        //nothing to do here: file update events are sent separately anyway
                    }
                }
                catch (Exception ex)
                {
                    await WriteException(ex, context);
                }
            }
        }

        //private async Task OnErrorAsync(IEventError ee, CancellationToken token)
        //{
        //    try
        //    { 
        //        await AddMessage(ConsoleColor.Red, $"[!]:{ee.Message}", context);
        //    }
        //    catch (Exception ex)
        //    {
        //        await WriteException(ex, context);
        //    }
        //}

        public static async Task AddMessage(ConsoleColor color, string message, Context context)
        {
            await Task.Run(() =>
            {
                lock (Lock)
                //using (await AsyncLock.LockAsync())
                {
                    try
                    {
                        Console.ForegroundColor = color;
                        Console.WriteLine($"[{context.Time.ToLocalTime():yyyy.MM.dd HH:mm:ss.ffff}]:{message}");
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        Console.ForegroundColor = _consoleColor;
                    }
                }
            }, context.Token);
        }

        public static async Task SaveFileModifications(string fullName, byte[] fileData, byte[] originalData, Context context)
        {
            var otherFullName = GetOtherFullName(fullName, context.ForHistory);


            //NB! detect whether the file actually changed
            var otherFileData = File.Exists(otherFullName)
                //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7
                ? await FileExtensions.ReadAllBytesAsync(@"\\?\" + otherFullName, context.Token) 
                : null;

            if (
                (otherFileData?.Length ?? -1) != fileData.Length
                || !FileExtensions.BinaryEqual(otherFileData, fileData)
            )
            {
                await DeleteFile(otherFullName, context);

                Directory.CreateDirectory(Path.GetDirectoryName(otherFullName));

                //@"\\?\" prefix is needed for writing to long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7
                await FileExtensions.WriteAllBytesAsync(@"\\?\" + otherFullName, fileData, context.Token);

                var now = DateTime.UtcNow;  //NB! compute now after saving the file
                BidirectionalSynchroniserSavedFileDates[otherFullName] = now;


                await AddMessage(ConsoleColor.Magenta, $"Synchronised updates from file {fullName}", context);
            }
            else if (false)
            {
                //touch the file
                var now = DateTime.UtcNow;  //NB! compute common now for ConverterSavedFileDates

                try
                {
                    File.SetLastWriteTimeUtc(otherFullName, now);
                }
                catch (Exception ex)
                {
                    await ConsoleWatch.WriteException(ex, context);
                }

                BidirectionalSynchroniserSavedFileDates[otherFullName] = now;
            }
        }   //public static async Task SaveFileModifications(string fullName, byte[] fileData, byte[] originalData, Context context)

#pragma warning restore AsyncFixer01
    }
}
