﻿//
// Copyright (c) Roland Pihlakas 2019 - 2020
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using myoddweb.directorywatcher;
using myoddweb.directorywatcher.interfaces;
using Nito.AspNetBackgroundTasks;
using Nito.AsyncEx;

namespace FolderSync
{
#pragma warning disable S2223   //Warning	S2223	Change the visibility of 'xxx' or make it 'const' or 'readonly'.
    internal static class Global
    {
        public static IConfigurationRoot Configuration;
        public static readonly CancellationTokenSource CancellationToken = new CancellationTokenSource();


        public static string SrcPath = "";


        public static bool EnableMirror = true;
        public static bool BidirectionalMirror = false;
        public static bool MirrorIgnoreSrcDeletions = false;
        public static bool MirrorIgnoreDestDeletions = false;

        public static bool UsePolling = false;
        public static int PollingDelay = 60;

        public static bool DoNotCompareFileContent = false;
        public static bool DoNotCompareFileDate = false;
        public static bool DoNotCompareFileSize = false;

        public static bool? CaseSensitiveFilenames = null;   //null: default behaviour depending on OS

        public static List<string> MirrorWatchedExtension = new List<string>() { "*" };

        public static List<string> MirrorExcludedExtensions = new List<string>() { "*~", "tmp" };
        public static List<string> MirrorIgnorePathsStartingWith = new List<string>();
        public static List<string> MirrorIgnorePathsContaining = new List<string>();

        public static string MirrorDestPath = "";



        public static bool EnableHistory = false;
        public static List<string> HistoryWatchedExtension = new List<string>() { "*" };

        public static string HistoryVersionFormat = "TIMESTAMP_BEFORE_EXT";
        public static string HistoryVersionSeparator = ".";

        public static List<string> HistoryExcludedExtensions = new List<string>() { "*~", "bak", "tmp" };
        public static List<string> HistoryIgnorePathsStartingWith = new List<string>();
        public static List<string> HistoryIgnorePathsContaining = new List<string>();

        public static string HistoryDestPath = "";



        public static long SrcPathMinFreeSpace = 0;
        public static long MirrorDestPathMinFreeSpace = 0;
        public static long HistoryDestPathMinFreeSpace = 0;



        internal static readonly AsyncLockQueueDictionary<string> FileOperationLocks = new AsyncLockQueueDictionary<string>();
        internal static readonly AsyncSemaphore FileOperationSemaphore = new AsyncSemaphore(2);     //allow 2 concurrent file synchronisations: while one is finishing the write, the next one can start the read
    }
#pragma warning restore S2223

    class DummyFileSystemEvent : IFileSystemEvent
    {
        [DebuggerStepThrough]
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

        public FileSystemInfo FileSystemInfo { [DebuggerStepThrough]get; }
        public string FullName { [DebuggerStepThrough]get; }
        public string Name { [DebuggerStepThrough]get; }
        public EventAction Action { [DebuggerStepThrough]get; }
        public EventError Error { [DebuggerStepThrough]get; }
        public DateTime DateTimeUtc { [DebuggerStepThrough]get; }
        public bool IsFile { [DebuggerStepThrough]get; }

        [DebuggerStepThrough]
        public bool Is(EventAction action)
        {
            return action == Action;
        }
    }

    internal class Program
    {
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



            Global.DoNotCompareFileContent = fileConfig.GetTextUpper("DoNotCompareFileContent") == "TRUE";   //default is false
            Global.DoNotCompareFileDate = fileConfig.GetTextUpper("DoNotCompareFileDate") == "TRUE";   //default is false
            Global.DoNotCompareFileSize = fileConfig.GetTextUpper("DoNotCompareFileSize") == "TRUE";   //default is false

            if (!string.IsNullOrWhiteSpace(fileConfig.GetTextUpper("CaseSensitiveFilenames")))
                Global.CaseSensitiveFilenames = fileConfig.GetTextUpper("CaseSensitiveFilenames") == "TRUE";   //default is false



            Global.SrcPath = fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "SrcPath");
            


            Global.EnableMirror = fileConfig.GetTextUpper("EnableMirror") != "FALSE";   //default is true
            Global.BidirectionalMirror = Global.EnableMirror && fileConfig.GetTextUpper("Bidirectional") == "TRUE";   //default is false
            Global.MirrorIgnoreSrcDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreSrcDeletions") == "TRUE";   //default is false
            Global.MirrorIgnoreDestDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreDestDeletions") == "TRUE";   //default is false

            Global.UsePolling = fileConfig.GetTextUpper("UsePolling") == "TRUE";   //default is false
            Global.PollingDelay = (int?)fileConfig.GetLong("PollingDelay") ?? Global.PollingDelay;



            Global.MirrorDestPath = fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorDestPath", "DestPath");

            Global.MirrorWatchedExtension = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorWatchedExtensions", "MirrorWatchedExtension", "WatchedExtensions", "WatchedExtension");

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            Global.MirrorExcludedExtensions = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorExcludedExtensions", "MirrorExcludedExtension", "ExcludedExtensions", "ExcludedExtension");   //NB! UpperOnWindows

            Global.MirrorIgnorePathsStartingWith = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsStartingWith", "IgnorePathsStartingWith");   //NB! UpperOnWindows
            Global.MirrorIgnorePathsContaining = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsContaining", "MirrorIgnorePathContaining", "IgnorePathsContaining", "IgnorePathContaining");   //NB! UpperOnWindows



            Global.EnableHistory = fileConfig.GetTextUpper("EnableHistory") == "TRUE";   //default is false

            Global.HistoryDestPath = fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryDestPath");

            Global.HistoryWatchedExtension = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryWatchedExtensions", "HistoryWatchedExtension", "WatchedExtensions", "WatchedExtension");

            Global.HistoryVersionFormat = fileConfig.GetTextUpper("HistoryVersionFormat") ?? "TIMESTAMP_BEFORE_EXT";
            Global.HistoryVersionSeparator = fileConfig.GetText("HistoryVersionSeparator") ?? ".";  //NB! no uppercase transformation here

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            Global.HistoryExcludedExtensions = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryExcludedExtensions", "HistoryExcludedExtension", "ExcludedExtensions", "ExcludedExtension");   //NB! UpperOnWindows

            Global.HistoryIgnorePathsStartingWith = fileConfig.GetListUpper("HistoryIgnorePathsStartingWith", "HistoryIgnorePathStartingWith", "IgnorePathsStartingWith", "IgnorePathStartingWith");   //NB! UpperOnWindows
            Global.HistoryIgnorePathsContaining = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryIgnorePathsContaining", "HistoryIgnorePathContaining", "IgnorePathsContaining", "IgnorePathContaining");   //NB! UpperOnWindows



            Global.SrcPathMinFreeSpace = fileConfig.GetLong("SrcPathMinFreeSpace") ?? 0;
            Global.MirrorDestPathMinFreeSpace = fileConfig.GetLong("MirrorDestPathMinFreeSpace") ?? 0;
            Global.HistoryDestPathMinFreeSpace = fileConfig.GetLong("HistoryDestPathMinFreeSpace") ?? 0;



            var pathHashes = "";
            //TODO!!! allow multiple instances with differet settings
            pathHashes += "_" + GetHashString(Global.SrcPath);
            pathHashes += "_" + GetHashString(Global.MirrorDestPath ?? "");
            pathHashes += "_" + GetHashString(Global.HistoryDestPath ?? "");

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

#if false
                var pollingOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                };
#endif

                //start the monitor.
                using (var watch = new Watcher())
                //using (var pollingSrcWatch = Global.UsePolling ? new PollingFileSystemWatcher(Global.SrcPath, "*", pollingOptions) : null)
                //using (var pollingDestWatch = Global.UsePolling && Global.BidirectionalMirror ? new PollingFileSystemWatcher(Global.MirrorDestPath, "*", pollingOptions) : null)
                {
                    watch.Add(new Request(Extensions.GetLongPath(Global.SrcPath), recursive: true));

                    if (Global.BidirectionalMirror)
                    {
                        watch.Add(new Request(Extensions.GetLongPath(Global.MirrorDestPath), recursive: true));
                    }


                    //prepare the console watcher so we can output pretty messages.
                    //var consoleWatch = new ConsoleWatch(watch, pollingSrcWatch, pollingDestWatch);
                    var consoleWatch = new ConsoleWatch(watch);


                    //start watching
                    //NB! start watching before synchronisation
                    watch.Start();


                    var initialSyncMessageContext = new Context(
                        eventObj: null,
                        token: Global.CancellationToken.Token,
                        forHistory: false,   //unused here
                        isSrcPath: false,   //unused here
                        isInitialScan: true
                    );


                    BackgroundTaskManager.Run(async () =>
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.White, "Doing initial synchronisation...", initialSyncMessageContext);

                        BackgroundTaskManager.Run(async () => 
                        { 
                            await InitialSyncCountdownEvent.WaitAsync(Global.CancellationToken.Token);

                            if (!Global.CancellationToken.IsCancellationRequested)
                                await ConsoleWatch.AddMessage(ConsoleColor.White, "Done initial synchronisation...", initialSyncMessageContext);
                        });

                        ScanFolders(isInitialScan: true);


                        if (Global.UsePolling)
                        {
                            while (!Global.CancellationToken.IsCancellationRequested)
                            { 
#if !NOASYNC
                                await Task.Delay(Global.PollingDelay * 1000, Global.CancellationToken.Token);     //TODO: config file?
#else
                                Global.CancellationToken.Token.WaitHandle.WaitOne(Global.PollingDelay * 1000);
#endif

                                ScanFolders(isInitialScan: false);
                            }
                        
                        }   //if (Global.UsePolling)

                    });     //BackgroundTaskManager.Run(async () =>


                    //listen for the Ctrl+C 
                    await WaitForCtrlC();

                    Console.WriteLine("Stopping...");

                    //stop everything.
                    watch.Stop();

                    Console.WriteLine("Exiting...");

                    GC.KeepAlive(consoleWatch);
                }
            }
            catch (Exception ex)
            {
                await WriteException(ex);
            }
        }   //private static async Task MainTask()

        private static AsyncCountdownEvent InitialSyncCountdownEvent = new AsyncCountdownEvent(1);

        private static Dictionary<string, FileInfo> HistorySrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorSrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorDestPrevFileInfos = new Dictionary<string, FileInfo>();

        private static void ScanFolders(bool isInitialScan)
        {
            bool keepFileInfosForLaterPolling = Global.UsePolling;

            //1. Do initial history synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableHistory)
            {
                ScanFolder(ref HistorySrcPrevFileInfos, Global.SrcPath, "*." + (Global.HistoryWatchedExtension.Count == 1 ? Global.HistoryWatchedExtension.Single() : "*"), forHistory: true, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
            }

            //1. Do initial mirror synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableMirror)
            {
                ScanFolder(ref MirrorSrcPrevFileInfos, Global.SrcPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
            }

            if (Global.BidirectionalMirror)
            {
                //2. Do initial mirror synchronisation from dest to src folder   //TODO: config for enabling and ordering of this operation
                ScanFolder(ref MirrorDestPrevFileInfos, Global.MirrorDestPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
            }

            if (isInitialScan)
                InitialSyncCountdownEvent.Signal();
        }

        private static void ScanFolder(ref Dictionary<string, FileInfo> PrevFileInfos, string path, string extension, bool forHistory, bool keepFileInfosForLaterPolling, bool isInitialScan)
        {
            var NewFileInfos = new Dictionary<string, FileInfo>();

            foreach (var fileInfo in ProcessSubDirs(new DirectoryInfo(Extensions.GetLongPath(path)), extension, forHistory))
            {
                NewFileInfos.Add(fileInfo.FullName, fileInfo);

                FileInfo prevFileInfo;
                if (!PrevFileInfos.TryGetValue(fileInfo.FullName, out prevFileInfo))
                {
                    if (isInitialScan)
                        InitialSyncCountdownEvent.AddCount();

                    BackgroundTaskManager.Run(async () => {

                        await ConsoleWatch.OnAddedAsync
                        (
                            new DummyFileSystemEvent(fileInfo),
                            Global.CancellationToken.Token,
                            isInitialScan
                        );

                        if (isInitialScan)
                            InitialSyncCountdownEvent.Signal();
                    });
                }
                else
                {
                    if (
                        prevFileInfo.Length != fileInfo.Length
                        || prevFileInfo.LastWriteTimeUtc != fileInfo.LastWriteTimeUtc

                        //TODO: support for copying the file attributes
                        //|| prevFileInfo.Attributes != fileInfo.Attributes   
                    )
                    {
                        if (isInitialScan)
                            InitialSyncCountdownEvent.AddCount();

                        BackgroundTaskManager.Run(async () => {

                            await ConsoleWatch.OnTouchedAsync
                            (
                                new DummyFileSystemEvent(fileInfo),
                                Global.CancellationToken.Token
                            );

                            if (isInitialScan)
                                InitialSyncCountdownEvent.Signal();
                        });
                    }
                }   //if (!PrevAddedFileInfos.TryGetValue(fileInfo.FullName, out prevFileInfo))
            }   //foreach (var fileInfo in ProcessSubDirs(new DirectoryInfo(Extensions.GetLongPath(path)), "*." + extension, forHistory))

            foreach (var fileInfoKvp in PrevFileInfos)
            {
                if (!NewFileInfos.ContainsKey(fileInfoKvp.Key))
                {
                    if (isInitialScan)
                        InitialSyncCountdownEvent.AddCount();

                    BackgroundTaskManager.Run(async () => {

                        await ConsoleWatch.OnRemovedAsync
                        (
                            new DummyFileSystemEvent(fileInfoKvp.Value),
                            Global.CancellationToken.Token
                        );

                        if (isInitialScan)
                            InitialSyncCountdownEvent.Signal();
                    });
                }
            }

            PrevFileInfos = NewFileInfos;

        }   //private static async Task ScanFolder(string path, string extension, bool forHistory, bool keepFileInfosForLaterPolling)

        private static IEnumerable<FileInfo> ProcessSubDirs(DirectoryInfo srcDirInfo, string searchPattern, bool forHistory, int recursionLevel = 0)
        {
#if false //this built-in functio will throw IOException in case some subfolder is an invalid reparse point
            return new DirectoryInfo(sourceDir)
                .GetFiles(searchPattern, SearchOption.AllDirectories);
#else
            FileInfo[] fileInfos;
            try
            {
                fileInfos = srcDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
            {
                //ignore exceptions due to long pathnames       //TODO: find a way to handle them
                fileInfos = Array.Empty<FileInfo>();
            }

            foreach (var fileInfo in fileInfos)
            {
                yield return fileInfo;
            }


            DirectoryInfo[] dirInfos;
#pragma warning disable S2327   //Warning	S2327	Combine this 'try' with the one starting on line XXX.
            try
            {
                dirInfos = srcDirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
            {
                //ignore exceptions due to long pathnames       //TODO: find a way to handle them
                dirInfos = Array.Empty<DirectoryInfo>();
            }
#pragma warning restore S2327

            foreach (var dirInfo in dirInfos)
            {
                //TODO: option to follow reparse points
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    continue;


                var nonFullNameInvariant = ConsoleWatch.GetNonFullName(dirInfo.FullName) + Path.PathSeparator;
                if (!forHistory)
                {
                    if (
                        Global.MirrorIgnorePathsStartingWith.Any(x => nonFullNameInvariant.StartsWith(x))
                        || Global.MirrorIgnorePathsContaining.Any(x => nonFullNameInvariant.Contains(x))
                    )
                    {
                        continue;
                    }
                }
                else
                {
                    if (
                        Global.HistoryIgnorePathsStartingWith.Any(x => nonFullNameInvariant.StartsWith(x))
                        || Global.HistoryIgnorePathsContaining.Any(x => nonFullNameInvariant.Contains(x))
                    )
                    {
                        continue;
                    }
                }


                var subDirFileInfos = ProcessSubDirs(dirInfo, searchPattern, forHistory, recursionLevel + 1);
                foreach (var subDirFileInfo in subDirFileInfos)
                {
                    yield return subDirFileInfo;
                }
            }   //foreach (var dirInfo in dirInfos)
#endif
        }   //private static IEnumerable<FileInfo> ProcessSubDirs(DirectoryInfo srcDirInfo, string searchPattern, bool forHistory, int recursionLevel = 0)

        private static async Task WriteException(Exception ex)
        {
            if (ex is AggregateException aggex)
            {
                await WriteException(aggex.InnerException);
                foreach (var aggexInner in aggex.InnerExceptions)
                {
                    await WriteException(aggexInner);
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


            var time = DateTime.Now;
            var msg = $"[{time:yyyy.MM.dd HH:mm:ss.ffff}]:{message}";
            await AddMessage(ConsoleColor.Red, msg, time, showAlert: true);            
        }

        private static async Task AddMessage(ConsoleColor color, string message, DateTime time, bool showAlert = false)
        {
            await Task.Run(() =>
            {
                lock (ConsoleWatch.Lock)
                {
                    try
                    {
                        Console.ForegroundColor = color;
                        Console.WriteLine(message);

                        if (
                            showAlert
                            && (ConsoleWatch.PrevAlertTime != time || ConsoleWatch.PrevAlertMessage != message)
                        )
                        {
                            MessageBox.Show(message, "FolderSync");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                    }
                    finally
                    {
                        Console.ForegroundColor = ConsoleWatch._consoleColor;
                    }
                }
            });
        }

        private static Task WaitForCtrlC()
        {
            var exitEvent = new AsyncManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                Global.CancellationToken.Cancel();
                e.Cancel = true;
                Console.WriteLine("Stop detected.");
                exitEvent.Set();
            };
            return exitEvent.WaitAsync();
        }
    }

    internal class Context
    {
        public readonly IFileSystemEvent Event;
        public readonly CancellationToken Token;
        public readonly bool ForHistory;
        public readonly bool IsSrcPath;
        public readonly bool IsInitialScan;

        public DateTime Time
        {
            get
            {
                return Event?.DateTimeUtc ?? DateTime.UtcNow;
            }
        }

#pragma warning disable CA1068  //should take CancellationToken as the last parameter
        public Context(IFileSystemEvent eventObj, CancellationToken token, bool forHistory, bool isSrcPath, bool isInitialScan)
#pragma warning restore CA1068
        {
            Event = eventObj;
            Token = token;
            ForHistory = forHistory;
            IsSrcPath = isSrcPath;
            IsInitialScan = isInitialScan;
        }
    }

    internal class ConsoleWatch
    {
        /// <summary>
        /// The original console color
        /// </summary>
        internal static readonly ConsoleColor _consoleColor = Console.ForegroundColor;

        /// <summary>
        /// We need a static lock so it is shared by all.
        /// </summary>
        internal static readonly object Lock = new object();
        //private static readonly AsyncLock AsyncLock = new AsyncLock();  //TODO: use this

        internal static DateTime PrevAlertTime;
        internal static string PrevAlertMessage;

//#pragma warning disable S2223   //Warning	S2223	Change the visibility of 'DoingInitialSync' or make it 'const' or 'readonly'.
//        public static bool DoingInitialSync = false;
//#pragma warning restore S2223

        private static ConcurrentDictionary<string, DateTime> BidirectionalSynchroniserSavedFileDates = new ConcurrentDictionary<string, DateTime>();
        private static readonly AsyncLockQueueDictionary<string> FileEventLocks = new AsyncLockQueueDictionary<string>();


#pragma warning disable S1118   //Warning	S1118	Hide this public constructor by making it 'protected'.
        public ConsoleWatch(IWatcher3 watch)
#pragma warning restore S1118
        {
            //_consoleColor = Console.ForegroundColor;

            //watch.OnErrorAsync += OnErrorAsync;
            watch.OnAddedAsync += (fse, token) => OnAddedAsync(fse, token, isInitialScan: false);
            watch.OnRemovedAsync += OnRemovedAsync;
            watch.OnRenamedAsync += OnRenamedAsync;
            watch.OnTouchedAsync += OnTouchedAsync;
        }

#if false
        private async Task OnErrorAsync(IEventError ee, CancellationToken token)
        {
            try
            {
                await AddMessage(ConsoleColor.Red, $"[!]:{ee.Message}", context);
            }
            catch (Exception ex)
            {
                await WriteException(ex, context);
            }
        }
#endif 

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


            var msg = $"[{context.Time.ToLocalTime():yyyy.MM.dd HH:mm:ss.ffff}] : {context.Event?.FullName} : {message}";
            await AddMessage(ConsoleColor.Red, msg, context, showAlert: true);
        }

        public static bool IsSrcPath(string fullNameInvariant)
        {
            return fullNameInvariant.StartsWith(Extensions.GetLongPath(Global.SrcPath));
        }

        public static string GetNonFullName(string fullName)
        {
            var fullNameInvariant = fullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            if (fullNameInvariant.StartsWith(Extensions.GetLongPath(Global.MirrorDestPath)))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.MirrorDestPath).Length);
            }
            else if (IsSrcPath(fullNameInvariant))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.SrcPath).Length);
            }
            else
            {
                throw new ArgumentException("Unexpected path provided to GetNonFullName()");
            }
        }

        public static string GetOtherFullName(string fullName, bool forHistory)
        {
            var fullNameInvariant = fullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var nonFullName = GetNonFullName(fullName);

            if (forHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    var srcFileDate = GetFileTime(fullName);    //NB! here read the current file time, not file time at the event

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
                if (fullNameInvariant.StartsWith(Extensions.GetLongPath(Global.MirrorDestPath)))
                {
                    return Path.Combine(Global.SrcPath, nonFullName);
                }
                else if (IsSrcPath(fullNameInvariant))
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
                fullName = Extensions.GetLongPath(fullName);

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

        public static bool NeedsUpdate(Context context)
        {
            if (
                (!Global.EnableMirror && !context.ForHistory)
                || (!Global.EnableHistory && context.ForHistory)
            )
            {
                return false;
            }

            //compare file date only when not doing initial sync OR when file content comparison is turned off
            if (context.IsInitialScan && !Global.DoNotCompareFileContent)
            {
                return true;
            }
            else    
            { 
                var synchroniserSaveDate = GetBidirectionalSynchroniserSaveDate(context.Event.FullName);
                var fileTime = context.Event.FileSystemInfo.LastWriteTimeUtc; //GetFileTime(fullName);

                if (
                    (!context.ForHistory && !Global.BidirectionalMirror)   //no need to debounce BIDIRECTIONAL file save events when bidirectional save is disabled 
                    || context.ForHistory
                    || fileTime > synchroniserSaveDate.AddSeconds(3)     //NB! ignore if the file changed during 3 seconds after bidirectional save   //TODO!! config
                )
                {
                    var otherFullName = GetOtherFullName(context.Event.FullName, context.ForHistory);

                    bool considerDateAsNewer = false;
                    if (Global.DoNotCompareFileDate)
                    { 
                        considerDateAsNewer = true;
                    }
                    else
                    { 
                        var otherFileTime = GetFileTime(otherFullName);

                        if (otherFileTime == DateTime.MinValue)     //file not found
                            return true;

                        if (fileTime > otherFileTime)     //NB!
                            considerDateAsNewer = true;
                    }

                    if (considerDateAsNewer)
                    { 
                        if (Global.DoNotCompareFileContent)
                        {
                            if (Global.DoNotCompareFileSize)
                            {
                                //if date check was done above then there is no need to do file existence check here
                                if (Global.DoNotCompareFileDate)
                                { 
                                    bool otherFileExists = File.Exists(Extensions.GetLongPath(otherFullName));
                                    
                                    if (!otherFileExists)
                                    {
                                        return true;
                                    }
                                }
                                else
                                {
                                    return false;   //if the other file does not exist then the function returns true in date check
                                }
                            }
                            else
                            {
                                //files from directory scan are passed as FileInfo type and have Length available
                                //but files from directory watcher are of type FileSystemInfo and do not have length available
                                var fileLength = (context.Event.FileSystemInfo as FileInfo)
                                                        ?.Length 
                                                        ?? GetFileSize(context.Event.FullName);
                                var otherFileLength = GetFileSize(otherFullName);

                                if (fileLength != otherFileLength)
                                {
                                    return true;
                                }
                            }
                        }
                        else    //if (Global.DoNotCompareFileContent)
                        { 
                            return true;
                        }
                    }   //if (fileTime > otherFileTime)
                }

                return false;

            }   //if (context.IsInitialScan && !Global.DoNotCompareFileContent)
        }

        public static async Task FileUpdated(string fullName, Context context)
        {
            if (
                IsWatchedFile(fullName, context.ForHistory)
                && NeedsUpdate(context)     //NB!
            )
            {
                var otherFullName = GetOtherFullName(fullName, context.ForHistory);
                using (await Global.FileOperationLocks.LockAsync(fullName, otherFullName, context.Token))
                {
                    using (await Global.FileOperationSemaphore.LockAsync())
                    { 
                        var fileData = await FileExtensions.ReadAllBytesAsync(Extensions.GetLongPath(fullName), context.Token);
                        var originalData = fileData;

                        //save without transformations
                        await ConsoleWatch.SaveFileModifications(fullName, fileData, originalData, context);
                    }
                }
            }
        }

        private static async Task FileDeleted(string fullName, Context context)
        {
            if (IsWatchedFile(fullName, context.ForHistory))
            {
                if (!File.Exists(Extensions.GetLongPath(fullName)))  //NB! verify that the file is still deleted
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
                fullName = Extensions.GetLongPath(fullName);

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

        private static long GetFileSize(string fullName)
        {
            try
            {
                fullName = Extensions.GetLongPath(fullName);

                if (File.Exists(fullName))
                    return new FileInfo(fullName).Length;
                else
                    return -1;
            }
            catch (FileNotFoundException)    //the file might have been deleted in the meanwhile
            {
                return -1;
            }
        }

        private static bool IsWatchedFile(string fullName, bool forHistory)
        {
            if (
                (!Global.EnableMirror && !forHistory)
                || (!Global.EnableHistory && forHistory)
            )
            {
                return false;
            }

            var fullNameInvariant = fullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            if (
                !forHistory
                &&
                (
                    Global.MirrorWatchedExtension.Any(x => fullNameInvariant.EndsWith("." + x))
                    || Global.MirrorWatchedExtension.Contains("*")
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
                    Global.HistoryWatchedExtension.Any(x => fullNameInvariant.EndsWith("." + x))
                    || Global.HistoryWatchedExtension.Contains("*")
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
            //NB! create separate context to properly handle disk free space checks on cases where file is renamed from src path to dest path (not a recommended practice though!)

            var previousFullNameInvariant = fse.PreviousFileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var previousContext = new Context(fse, token, forHistory: false, isSrcPath: IsSrcPath(previousFullNameInvariant), isInitialScan: false);

            var newFullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var newPathIsSrcPath = IsSrcPath(newFullNameInvariant);


            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: newPathIsSrcPath, isInitialScan: false),
                new Context(fse, token, forHistory: true, isSrcPath: newPathIsSrcPath, isInitialScan: false)
            };


            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        var prevFileIsWatchedFile = IsWatchedFile(fse.PreviousFileSystemInfo.FullName, context.ForHistory);
                        var newFileIsWatchedFile = IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory);

                        if (prevFileIsWatchedFile
                            || newFileIsWatchedFile)
                        {
                            await AddMessage(ConsoleColor.Cyan, $"[{(fse.IsFile ? "F" : "D")}][R]:{fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", context);

                            //NB! if file is renamed to cs~ or resx~ then that means there will be yet another write to same file, so lets skip this event here - NB! skip the event here, including delete event of the previous file
                            if (!fse.FileSystemInfo.FullName.EndsWith("~"))
                            {
                                //using (await Global.FileOperationLocks.LockAsync(rfse.FileSystemInfo.FullName, rfse.PreviousFileSystemInfo.FullName, context.Token))  //comment-out: prevent deadlock
                                {
                                    if (newFileIsWatchedFile)
                                    {
                                        await FileUpdated(fse.FileSystemInfo.FullName, context);
                                    }

                                    if (prevFileIsWatchedFile)
                                    {
                                        if (
                                            !context.ForHistory     //history files have a different name format than the original file names and would not cause a undefined behaviour
                                            && newFileIsWatchedFile     //both files were watched files
                                            && previousContext.IsSrcPath != context.IsSrcPath    
                                            && 
                                            (
                                                Global.BidirectionalMirror      //move in either direction between src and mirrorDest
                                                || previousContext.IsSrcPath    //src -> mirrorDest move
                                            )
                                        )
                                        {
                                            //the file was moved from one watched path to another watched path, which is illegal, lets ignore the file move

                                            await AddMessage(ConsoleColor.Red, $"Ignoring file delete in the source path since the move was to the other managed path : {fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", previousContext);
                                        }
                                        else
                                        {
                                            await FileDeleted(fse.PreviousFileSystemInfo.FullName, previousContext);
                                        }
                                    }
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

        internal static async Task OnRemovedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: IsSrcPath(fullNameInvariant), isInitialScan: false),
                new Context(fse, token, forHistory: true, isSrcPath: true, isInitialScan: false)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (
                        !context.ForHistory 
                        && 
                        (
                            (context.IsSrcPath && Global.MirrorIgnoreSrcDeletions)
                            || (!context.IsSrcPath && Global.MirrorIgnoreDestDeletions)
                        )
                    )
                    { 
                        continue;
                    }

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

        internal static async Task OnAddedAsync(IFileSystemEvent fse, CancellationToken token, bool isInitialScan)
        {
            var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: IsSrcPath(fullNameInvariant), isInitialScan: isInitialScan),
                new Context(fse, token, forHistory: true, isSrcPath: true, isInitialScan: isInitialScan)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            if (!context.IsInitialScan)
                                await AddMessage(ConsoleColor.Green, $"[{(fse.IsFile ? "F" : "D")}][+]:{fse.FileSystemInfo.FullName}", context);

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

        internal static async Task OnTouchedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: IsSrcPath(fullNameInvariant), isInitialScan: false),
                new Context(fse, token, forHistory: true, isSrcPath: true, isInitialScan: false)
            };

            foreach (var context in contexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory))
                        {
                            //check for file type only after checking IsWatchedFile first since file type checking might already be a slow operation
                            if (File.Exists(Extensions.GetLongPath(fse.FileSystemInfo.FullName)))     //for some reason fse.IsFile is set even for folders
                            { 
                                await AddMessage(ConsoleColor.Gray, $"[{(fse.IsFile ? "F" : "D")}][T]:{fse.FileSystemInfo.FullName}", context);

                                using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                                {
                                    await FileUpdated(fse.FileSystemInfo.FullName, context);
                                }
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

        public static async Task AddMessage(ConsoleColor color, string message, Context context, bool showAlert = false)
        {
            await Task.Run(() =>
            {
                lock (Lock)
                //using (await AsyncLock.LockAsync())
                {
                    try
                    {
                        Console.ForegroundColor = color;
                        Console.WriteLine(message);

                        if (
                            showAlert
                            && (PrevAlertTime != context.Time || PrevAlertMessage != message)
                        )
                        {
                            PrevAlertTime = context.Time;
                            PrevAlertMessage = message;

                            MessageBox.Show(message, "FolderSync");
                        }
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
            var otherFileData = 
                !Global.DoNotCompareFileContent 
                    && File.Exists(Extensions.GetLongPath(otherFullName))
                ? await FileExtensions.ReadAllBytesAsync(Extensions.GetLongPath(otherFullName), context.Token)    //TODO: optimisation: no need to read the bytes in case the file lenghts are different
                : null;

            if (
                (
                    !Global.DoNotCompareFileContent
                    &&
                    (
                        (otherFileData?.Length ?? -1) != fileData.Length
                        || !FileExtensions.BinaryEqual(otherFileData, fileData)
                    )
                )
                ||
                Global.DoNotCompareFileContent
            )
            {
                var minDiskFreeSpace = context.ForHistory ? Global.HistoryDestPathMinFreeSpace : (context.IsSrcPath ? Global.MirrorDestPathMinFreeSpace : Global.SrcPathMinFreeSpace);
                var actualFreeSpace = minDiskFreeSpace > 0 ? CheckDiskSpace(otherFullName) : 0;
                if (minDiskFreeSpace > actualFreeSpace - fileData.Length)
                {
                    await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {fullName} : minDiskFreeSpace > actualFreeSpace : {minDiskFreeSpace} > {actualFreeSpace}", context);

                    return;
                }


                await DeleteFile(otherFullName, context);

                var otherDirName = Path.GetDirectoryName(otherFullName);
                if (!Directory.Exists(Extensions.GetLongPath(otherDirName)))
                    Directory.CreateDirectory(Extensions.GetLongPath(otherDirName));

                await FileExtensions.WriteAllBytesAsync(Extensions.GetLongPath(otherFullName), fileData, context.Token);

                var now = DateTime.UtcNow;  //NB! compute now after saving the file
                BidirectionalSynchroniserSavedFileDates[otherFullName] = now;


                await AddMessage(ConsoleColor.Magenta, $"Synchronised updates from file {fullName}", context);
            }
            else if (false)     //TODO: config
            {
                //touch the file
                var now = DateTime.UtcNow;  //NB! compute common now for ConverterSavedFileDates

                try
                {
                    File.SetLastWriteTimeUtc(Extensions.GetLongPath(otherFullName), now);
                }
                catch (Exception ex)
                {
                    await ConsoleWatch.WriteException(ex, context);
                }

                BidirectionalSynchroniserSavedFileDates[otherFullName] = now;
            }
        }   //public static async Task SaveFileModifications(string fullName, byte[] fileData, byte[] originalData, Context context)

        public static long? CheckDiskSpace(string path)
        {
            long? freeBytes = null;

            try     //NB! on some drives (for example, RAM drives, GetDiskFreeSpaceEx does not work
            {
                //NB! DriveInfo works on paths well in Linux    //TODO: what about Mac?
                var drive = new DriveInfo(path);
                freeBytes = drive.AvailableFreeSpace;
            }
            catch (ArgumentException)
            {
                if (ConfigParser.IsWindows)
                {
                    long freeBytesOut;
                    if (WindowsDllImport.GetDiskFreeSpaceEx(path, out freeBytesOut, out var _, out var __))
                        freeBytes = freeBytesOut;
                }
            }

            return freeBytes;
        }

#pragma warning restore AsyncFixer01
    }

    internal static class WindowsDllImport  //keep in a separate class just in case to ensure that dllimport is not attempted during application loading under non-Windows OS
    {
        //https://stackoverflow.com/questions/61037184/find-out-free-and-total-space-on-a-network-unc-path-in-netcore-3-x
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
            out long lpFreeBytesAvailable,
            out long lpTotalNumberOfBytes,
            out long lpTotalNumberOfFreeBytes);
    }
}
