﻿//
// Copyright (c) Roland Pihlakas 2019 - 2022
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
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dasync.Collections;
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



        public static bool UseIdlePriority = false;
        public static int FileWriteDelayMs = 0;
        public static int WriteBufferKB = 0;
        public static int BufferWriteDelayMs = 0;


        public static bool UsePolling = false;
        public static int PollingDelay = 60; 
        
        
        public static int RetryCountOnEmptyDirlist = 0;


        public static string SrcPath = "";


        public static bool EnableMirror = true;
        public static bool BidirectionalMirror = false;
        public static bool MirrorIgnoreSrcDeletions = false;
        public static bool MirrorIgnoreDestDeletions = false;

        public static long MaxFileSizeMB = 2048;
        public static bool DoNotCompareFileContent = false;
        public static bool DoNotCompareFileDate = false;
        public static bool DoNotCompareFileSize = false;

        public static bool CacheDestAndHistoryFolders = false;   //default is false since it consumes memory 
        public static bool PersistentCacheDestAndHistoryFolders = false;
        public static string CachePath = "";

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

        internal static readonly ConcurrentDictionary<string, bool> CreatedFoldersCache = new ConcurrentDictionary<string, bool>();
        internal static readonly ConcurrentDictionary<string, CachedFileInfo> DestAndHistoryFileInfoCache = new ConcurrentDictionary<string, CachedFileInfo>();
        internal static readonly AsyncLockQueueDictionary<string> PersistentCacheLocks = new AsyncLockQueueDictionary<string>();
    }
#pragma warning restore S2223

    [Serializable]
    class CachedFileInfo
    {
        public long? Length { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }
        public bool? Exists { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public DateTime CreationTimeUtc { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }
        public DateTime LastWriteTimeUtc { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public string FullName { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public FileAttributes Attributes { [DebuggerStepThrough]get; [DebuggerStepThrough]set; }

        public CachedFileInfo(string fullName, long length, DateTime lastWriteTimeUtc)
        {
            Exists = true;
            Length = length;

            CreationTimeUtc = lastWriteTimeUtc;
            LastWriteTimeUtc = lastWriteTimeUtc;
            FullName = fullName;
            Attributes = FileAttributes.Normal;
        }

        public CachedFileInfo(CachedFileInfo fileInfo, bool useNonFullPath)
        {
            Exists = fileInfo.Exists;
            Length = fileInfo.Length;

            CreationTimeUtc = fileInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            FullName = useNonFullPath ? ConsoleWatch.GetNonFullName(fileInfo.FullName) : fileInfo.FullName;
            Attributes = fileInfo.Attributes;
        }

        public CachedFileInfo(FileInfo fileInfo)
        {
            Exists = fileInfo.Exists;
            Length = Exists == true ? (long?)fileInfo.Length : null;   //need to check for exists else exception occurs during reading length

            CreationTimeUtc = fileInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            FullName = fileInfo.FullName;
            Attributes = fileInfo.Attributes;
        }

        public CachedFileInfo(FileSystemInfo fileSystemInfo)
        {
            var fileInfo = fileSystemInfo as FileInfo;

            Exists = fileInfo?.Exists;
            Length = Exists == true ? fileInfo?.Length : null;

            CreationTimeUtc = fileSystemInfo.CreationTimeUtc;
            LastWriteTimeUtc = fileSystemInfo.LastWriteTimeUtc;
            FullName = fileSystemInfo.FullName;
            Attributes = fileSystemInfo.Attributes;
        }
    }   //private class CachedFileInfo : ISerializable

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



            Global.UsePolling = fileConfig.GetTextUpper("UsePolling") == "TRUE";   //default is false
            Global.PollingDelay = (int?)fileConfig.GetLong("PollingDelay") ?? Global.PollingDelay;


            Global.RetryCountOnEmptyDirlist = (int?)fileConfig.GetLong("RetryCountOnEmptyDirlist") ?? Global.RetryCountOnEmptyDirlist;


            Global.UseIdlePriority = fileConfig.GetTextUpper("UseIdlePriority") == "TRUE";   //default is false
            Global.FileWriteDelayMs = (int?)fileConfig.GetLong("FileWriteDelayMs") ?? Global.FileWriteDelayMs;
            Global.WriteBufferKB = (int?)fileConfig.GetLong("WriteBufferKB") ?? Global.WriteBufferKB;
            Global.BufferWriteDelayMs = (int?)fileConfig.GetLong("BufferWriteDelayMs") ?? Global.BufferWriteDelayMs;


            Global.MaxFileSizeMB = fileConfig.GetLong("MaxFileSizeMB") ?? Global.MaxFileSizeMB;
            Global.DoNotCompareFileContent = fileConfig.GetTextUpper("DoNotCompareFileContent") == "TRUE";   //default is false
            Global.DoNotCompareFileDate = fileConfig.GetTextUpper("DoNotCompareFileDate") == "TRUE";   //default is false
            Global.DoNotCompareFileSize = fileConfig.GetTextUpper("DoNotCompareFileSize") == "TRUE";   //default is false

            //NB! these two options are independent!
            Global.CacheDestAndHistoryFolders = fileConfig.GetTextUpper("CacheDestAndHistoryFolders") == "TRUE";   //default is false since it consumes memory 
            Global.PersistentCacheDestAndHistoryFolders = fileConfig.GetTextUpper("PersistentCacheDestAndHistoryFolders") == "TRUE";   //default is false
            
            Global.CachePath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "CachePath"));
            if (!string.IsNullOrWhiteSpace(Global.CachePath))
                Global.CachePath = Extensions.GetDirPathWithTrailingSlash(Path.Combine(".", "cache")).ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);


            if (!string.IsNullOrWhiteSpace(fileConfig.GetTextUpper("CaseSensitiveFilenames")))   //default is null
                Global.CaseSensitiveFilenames = fileConfig.GetTextUpper("CaseSensitiveFilenames") == "TRUE";
            

            Global.SrcPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "SrcPath"));
            
            
            Global.EnableMirror = fileConfig.GetTextUpper("EnableMirror") != "FALSE";   //default is true
            Global.BidirectionalMirror = Global.EnableMirror && fileConfig.GetTextUpper("Bidirectional") == "TRUE";   //default is false
            Global.MirrorIgnoreSrcDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreSrcDeletions") == "TRUE";   //default is false
            Global.MirrorIgnoreDestDeletions = Global.EnableMirror && fileConfig.GetTextUpper("MirrorIgnoreDestDeletions") == "TRUE";   //default is false


            Global.MirrorDestPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorDestPath", "DestPath"));

            Global.MirrorWatchedExtension = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorWatchedExtensions", "MirrorWatchedExtension", "WatchedExtensions", "WatchedExtension");

            //this would need Microsoft.Extensions.Configuration and Microsoft.Extensions.Configuration.Binder packages
            Global.MirrorExcludedExtensions = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorExcludedExtensions", "MirrorExcludedExtension", "ExcludedExtensions", "ExcludedExtension");   //NB! UpperOnWindows

            Global.MirrorIgnorePathsStartingWith = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsStartingWith", "IgnorePathsStartingWith");   //NB! UpperOnWindows
            Global.MirrorIgnorePathsContaining = fileConfig.GetListUpperOnWindows(Global.CaseSensitiveFilenames, "MirrorIgnorePathsContaining", "MirrorIgnorePathContaining", "IgnorePathsContaining", "IgnorePathContaining");   //NB! UpperOnWindows



            Global.EnableHistory = fileConfig.GetTextUpper("EnableHistory") == "TRUE";   //default is false

            Global.HistoryDestPath = Extensions.GetDirPathWithTrailingSlash(fileConfig.GetTextUpperOnWindows(Global.CaseSensitiveFilenames, "HistoryDestPath"));

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


                if (Global.UseIdlePriority)
                {
                    try
                    { 
                        var CurrentProcess = Process.GetCurrentProcess();
                        CurrentProcess.PriorityClass = ProcessPriorityClass.Idle; 
                        CurrentProcess.PriorityBoostEnabled = false;

                        if (ConfigParser.IsWindows)
                        { 
                            WindowsDllImport.SetIOPriority(CurrentProcess.Handle, WindowsDllImport.PROCESSIOPRIORITY.PROCESSIOPRIORITY_VERY_LOW);
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unable to set idle priority.");
                    }
                }


                ThreadPool.SetMaxThreads(24, 24);   //TODO: config


                //start the monitor.
                using (var watch = new Watcher())
                {
                    watch.Add(new Request(Extensions.GetLongPath(Global.SrcPath), recursive: true));

                    if (Global.BidirectionalMirror)
                    {
                        watch.Add(new Request(Extensions.GetLongPath(Global.MirrorDestPath), recursive: true));
                    }


                    //prepare the console watcher so we can output pretty messages.
                    var consoleWatch = new ConsoleWatch(watch);


                    //start watching
                    //NB! start watching before synchronisation
                    watch.Start();


                    var initialSyncMessageContext = new Context(
                        eventObj: null,
                        token: Global.CancellationToken.Token,
                        forHistory: false,   //unused here
                        isSrcPath: false,   //unused here
                        isInitialScan: true,
                        fileInfoRefreshedBoolRef: null
                    );


                    BackgroundTaskManager.Run(async () =>
                    {
                        await ConsoleWatch.AddMessage(ConsoleColor.White, "Doing initial synchronisation...", initialSyncMessageContext);

                        await ScanFolders(isInitialScan: true);

                        BackgroundTaskManager.Run(async () =>
                        {
                            await InitialSyncCountdownEvent.WaitAsync(Global.CancellationToken.Token);

                            //if (!Global.CancellationToken.IsCancellationRequested)
                                await ConsoleWatch.AddMessage(ConsoleColor.White, "Done initial synchronisation...", initialSyncMessageContext);
                        });


                        if (Global.UsePolling)
                        {
                            while (!Global.CancellationToken.IsCancellationRequested)
                            { 
#if !NOASYNC
                                await Task.Delay(Global.PollingDelay * 1000, Global.CancellationToken.Token);
#else
                                Global.CancellationToken.Token.WaitHandle.WaitOne(Global.PollingDelay * 1000);
#endif

                                await ScanFolders(isInitialScan: false);
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


                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                await WriteException(ex);
            }
        }   //private static async Task MainTask()

        private static readonly AsyncCountdownEvent InitialSyncCountdownEvent = new AsyncCountdownEvent(1);

        private static Dictionary<string, FileInfo> HistorySrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorSrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorDestPrevFileInfos = new Dictionary<string, FileInfo>();

        private static async Task ScanFolders(bool isInitialScan)
        {
            bool keepFileInfosForLaterPolling = Global.UsePolling;

            //1. Do initial history synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableHistory)
            {
                var historySrcPrevFileInfosRef = new FileInfosRef(HistorySrcPrevFileInfos);
                await ScanFolder(historySrcPrevFileInfosRef, Global.SrcPath, "*." + (Global.HistoryWatchedExtension.Count == 1 ? Global.HistoryWatchedExtension.Single() : "*"), isSrcPath: true, forHistory: true, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
                HistorySrcPrevFileInfos = historySrcPrevFileInfosRef.Value;
            }

            //2. Do initial mirror synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableMirror)
            {
                var mirrorSrcPrevFileInfosRef = new FileInfosRef(MirrorSrcPrevFileInfos);
                await ScanFolder(mirrorSrcPrevFileInfosRef, Global.SrcPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), isSrcPath: true, forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
                MirrorSrcPrevFileInfos = mirrorSrcPrevFileInfosRef.Value;
            }

            if (Global.BidirectionalMirror)
            {
                //3. Do initial mirror synchronisation from dest to src folder   //TODO: config for enabling and ordering of this operation
                var mirrorDestPrevFileInfosRef = new FileInfosRef(MirrorDestPrevFileInfos);
                await ScanFolder(mirrorDestPrevFileInfosRef, Global.MirrorDestPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), isSrcPath: false, forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, isInitialScan: isInitialScan);
                MirrorDestPrevFileInfos = mirrorDestPrevFileInfosRef.Value;
            }

            if (isInitialScan)
                InitialSyncCountdownEvent.Signal();
        }

        private class FileInfosRef
        {
            public Dictionary<string, FileInfo> Value;

            [DebuggerStepThrough]
            public FileInfosRef(Dictionary<string, FileInfo> value)
            {
                Value = value;
            }
        }

        private static async Task ScanFolder(FileInfosRef PrevFileInfos, string path, string extension, bool isSrcPath, bool forHistory, bool keepFileInfosForLaterPolling, bool isInitialScan)
        {
            var NewFileInfos = !Global.MirrorIgnoreSrcDeletions ? new Dictionary<string, FileInfo>() : null;

            var fileInfos = ProcessSubDirs(new DirectoryInfo(Extensions.GetLongPath(path)), extension, isSrcPath, forHistory);
            await fileInfos.ForEachAsync(fileInfo => 
            {
                if (!Global.MirrorIgnoreSrcDeletions)
                    NewFileInfos.Add(fileInfo.FullName, fileInfo);

                FileInfo prevFileInfo;
                if (!PrevFileInfos.Value.TryGetValue(fileInfo.FullName, out prevFileInfo))
                {
                    if (Global.MirrorIgnoreSrcDeletions)
                        PrevFileInfos.Value.Add(fileInfo.FullName, fileInfo);

                    if (isInitialScan)
                        InitialSyncCountdownEvent.AddCount();

                    BackgroundTaskManager.Run(async () => 
                    {
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
                        if (Global.MirrorIgnoreSrcDeletions)
                            PrevFileInfos.Value[fileInfo.FullName] = fileInfo;  //NB! update the file info

                        BackgroundTaskManager.Run(async () => 
                        {
                            await ConsoleWatch.OnTouchedAsync
                            (
                                new DummyFileSystemEvent(fileInfo),
                                Global.CancellationToken.Token
                            );
                        });
                    }
                }   //if (!PrevAddedFileInfos.TryGetValue(fileInfo.FullName, out prevFileInfo))
            });   //await fileInfos.ForEachAsync(async fileInfo => {

            if (!Global.MirrorIgnoreSrcDeletions)
            { 
                foreach (var fileInfoKvp in PrevFileInfos.Value)
                {
                    if (!NewFileInfos.ContainsKey(fileInfoKvp.Key))
                    {
                        BackgroundTaskManager.Run(async () => 
                        {
                            await ConsoleWatch.OnRemovedAsync
                            (
                                new DummyFileSystemEvent(fileInfoKvp.Value),
                                Global.CancellationToken.Token
                            );
                        });
                    }
                }

                PrevFileInfos.Value = NewFileInfos;

            }   //if (!Global.MirrorIgnoreSrcDeletions)

        }   //private static async Task ScanFolder(string path, string extension, bool forHistory, bool keepFileInfosForLaterPolling)

        private static IAsyncEnumerable<FileInfo> ProcessSubDirs(DirectoryInfo srcDirInfo, string searchPattern, bool isSrcPath, bool forHistory, int recursionLevel = 0)
        {
            return new AsyncEnumerable<FileInfo>(async yield => {

#if false //this built-in functio will throw IOException in case some subfolder is an invalid reparse point
                return new DirectoryInfo(sourceDir)
                    .GetFiles(searchPattern, SearchOption.AllDirectories);
#else

                //Directory.GetFileSystemEntries would not help here since it returns only strings, not FileInfos

                //TODO: under Windows10 use https://github.com/ljw1004/uwp-desktop for true async dirlists


                var destFileInfosDict = new Dictionary<string, CachedFileInfo>();
                var destFileInfosTask = Task.CompletedTask;

                var historyFileInfosDict = new Dictionary<string, CachedFileInfo>();
                var historyFileInfosTask = Task.CompletedTask;

                AsyncLockQueueDictionary<string>.LockDictReleaser destDirCacheLock = null;
                AsyncLockQueueDictionary<string>.LockDictReleaser historyDirCacheLock = null;

                FileInfo[] fileInfos = null;
                DirectoryInfo[] dirInfos = null;

                bool updateDestDirPersistentCache = false;
                bool updateHistoryDirPersistentCache = false;

                try     //finally for destDirCacheLock and historyDirCacheLock
                { 
                    if (Global.CacheDestAndHistoryFolders/* || Global.PersistentCacheDestAndHistoryFolders*/)
                    {
                        if (
                            !Global.BidirectionalMirror 
                            && Global.EnableMirror && isSrcPath && !forHistory
                        )
                        {
                            destFileInfosTask = Task.Run(async () =>
                            { 
                                try
                                {
                                    var destDirName = Extensions.GetLongPath(ConsoleWatch.GetOtherDirName(srcDirInfo.FullName, forHistory));
                                    destDirCacheLock = await Global.PersistentCacheLocks.LockAsync(destDirName, Global.CancellationToken.Token);
                                    var destFileInfos = (await ConsoleWatch.ReadFileInfoCache(destDirName, forHistory))?.Values.ToList();
                                
                                    if (destFileInfos == null)
                                    {
                                        updateDestDirPersistentCache = true;

                                        var destDirInfo = new DirectoryInfo(destDirName);

                                        if (await Extensions.FSOperation(() => Directory.Exists(destDirName), Global.CancellationToken.Token))
                                        { 
                                            destFileInfos = (await Extensions.DirListOperation
                                            (
                                                () => destDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly),
                                                Global.RetryCountOnEmptyDirlist,
                                                Global.CancellationToken.Token
                                            ))
                                            .Select(x => new CachedFileInfo(x))
                                            .ToList();
                                        }
                                        else
                                        {
                                            destFileInfos = new List<CachedFileInfo>();
                                        }
                                    }
                                    else   //if (destFileInfos == null)
                                    {
                                        foreach (var fileInfo in destFileInfos)
                                        {
                                            fileInfo.FullName = Path.Combine(Global.MirrorDestPath, fileInfo.FullName);
                                        }
                                    }


                                    if (
                                        !Global.CreatedFoldersCache.ContainsKey(destDirName)
                                        && destFileInfos.Any(x => x.Exists == true)
                                    )
                                    {
                                        Global.CreatedFoldersCache.TryAdd(destDirName, true);
                                    }


                                    //Google Drive can have multiple files with same name
                                    foreach (var fileInfo in destFileInfos)
                                    {
                                        var longFullName = Extensions.GetLongPath(fileInfo.FullName);

                                        CachedFileInfo prevFileInfo;
                                        if (destFileInfosDict.TryGetValue(longFullName, out prevFileInfo))
                                        {
                                            if (
                                                (prevFileInfo.LastWriteTimeUtc > fileInfo.LastWriteTimeUtc)
                                                ||
                                                (
                                                    (prevFileInfo.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
                                                    && (prevFileInfo.CreationTimeUtc > fileInfo.CreationTimeUtc)
                                                )
                                            )
                                            { 
                                                continue;
                                            }
                                        }

                                        destFileInfosDict[longFullName] = fileInfo;
                                    }
                                }
                                catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                                {
                                    //ignore the error
                                }
                            })
                            .WaitAsync(Global.CancellationToken.Token);
                        }


                        if (Global.EnableHistory && isSrcPath && forHistory)
                        {
                            historyFileInfosTask = Task.Run(async () =>
                            {
                                try
                                {
                                    var historyDirName = Extensions.GetLongPath(ConsoleWatch.GetOtherDirName(srcDirInfo.FullName, forHistory));
                                    historyDirCacheLock = await Global.PersistentCacheLocks.LockAsync(historyDirName, Global.CancellationToken.Token);
                                    var historyFileInfos = (await ConsoleWatch.ReadFileInfoCache(historyDirName, forHistory))?.Values.ToList();

                                    if (historyFileInfos == null)
                                    {
                                        updateHistoryDirPersistentCache = true;

                                        var historyDirInfo = new DirectoryInfo(historyDirName);

                                        if (await Extensions.FSOperation(() => Directory.Exists(historyDirName), Global.CancellationToken.Token))
                                        {
                                            historyFileInfos = (await Extensions.DirListOperation
                                            (
                                                () => historyDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly),
                                                Global.RetryCountOnEmptyDirlist,
                                                Global.CancellationToken.Token
                                            ))
                                            .Select(x => new CachedFileInfo(x))
                                            .ToList();
                                        }
                                        else
                                        {
                                            historyFileInfos = new List<CachedFileInfo>();
                                        }
                                    }
                                    else   //if (historyFileInfos == null)
                                    {
                                        foreach (var fileInfo in historyFileInfos)
                                        {
                                            fileInfo.FullName = Path.Combine(Global.HistoryDestPath, fileInfo.FullName);
                                        }
                                    }


                                    if (
                                        !Global.CreatedFoldersCache.ContainsKey(historyDirName)
                                        && historyFileInfos.Any(x => x.Exists == true)
                                    )
                                    {
                                        Global.CreatedFoldersCache.TryAdd(historyDirName, true);
                                    }


                                    //Google Drive can have multiple files with same name
                                    foreach (var fileInfo in historyFileInfos)
                                    {
                                        var longFullName = Extensions.GetLongPath(fileInfo.FullName);

                                        CachedFileInfo prevFileInfo;
                                        if (historyFileInfosDict.TryGetValue(longFullName, out prevFileInfo))
                                        {
                                            if (
                                                (prevFileInfo.LastWriteTimeUtc > fileInfo.LastWriteTimeUtc)
                                                ||
                                                (
                                                    (prevFileInfo.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
                                                    && (prevFileInfo.CreationTimeUtc > fileInfo.CreationTimeUtc)
                                                )
                                            )
                                            {
                                                continue;
                                            }
                                        }

                                        historyFileInfosDict[longFullName] = fileInfo;
                                    }
                                }
                                catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                                {
                                    //ignore the error
                                }
                            })
                            .WaitAsync(Global.CancellationToken.Token);
                        }
                    }   //if (Global.CacheDestAndHistoryFolders)


                    var fileInfosTask = Task.Run(async () =>
                    {
                        try
                        {
                            fileInfos = await Extensions.DirListOperation
                            (
                                () => srcDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly),
                                Global.RetryCountOnEmptyDirlist,
                                Global.CancellationToken.Token
                            );
                        }
                        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                        {
                            fileInfos = Array.Empty<FileInfo>();
                        }
                    })
                    .WaitAsync(Global.CancellationToken.Token);


                    var dirInfosTask = Task.Run(async () =>
                    {
                        try
                        {
                            dirInfos = await Extensions.DirListOperation
                            (
                                () => srcDirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly),
                                Global.RetryCountOnEmptyDirlist,
                                Global.CancellationToken.Token
                            );
                        }
                        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                        {
                            dirInfos = Array.Empty<DirectoryInfo>();
                        }
                    })
                    .WaitAsync(Global.CancellationToken.Token);


                    await Task.WhenAll(destFileInfosTask, historyFileInfosTask, fileInfosTask, dirInfosTask);


                    if (Global.CacheDestAndHistoryFolders/* || Global.PersistentCacheDestAndHistoryFolders*/)
                    {
                        var destFileInfosToCache = new Dictionary<string, CachedFileInfo>();
                        var historyFileInfosToCache = new Dictionary<string, CachedFileInfo>();

                        foreach (var fileInfo in fileInfos)
                        {
                            if (
                                !Global.BidirectionalMirror 
                                && Global.EnableMirror && isSrcPath && !forHistory
                            )
                            {
                                var destFileFullName = await ConsoleWatch.GetOtherFullName(fileInfo, forHistory);
                                destFileFullName = Extensions.GetLongPath(destFileFullName);

                                CachedFileInfo destFileInfo;
                                if (destFileInfosDict.TryGetValue(destFileFullName, out destFileInfo))
                                { 
                                    Global.DestAndHistoryFileInfoCache[destFileFullName] = destFileInfo;

                                    if (Global.PersistentCacheDestAndHistoryFolders)
                                    { 
                                        destFileInfosToCache[ConsoleWatch.GetNonFullName(destFileFullName)] 
                                            = new CachedFileInfo(destFileInfo, useNonFullPath: true);
                                    }
                                }
                            }


                            if (Global.EnableHistory && isSrcPath && forHistory)
                            {
                                var historyFileFullName = await ConsoleWatch.GetOtherFullName(fileInfo, forHistory);
                                historyFileFullName = Extensions.GetLongPath(historyFileFullName);

                                CachedFileInfo historyFileInfo;
                                if (historyFileInfosDict.TryGetValue(historyFileFullName, out historyFileInfo))
                                { 
                                    Global.DestAndHistoryFileInfoCache[historyFileFullName] = historyFileInfo;

                                    if (Global.PersistentCacheDestAndHistoryFolders)
                                    { 
                                        historyFileInfosToCache[ConsoleWatch.GetNonFullName(historyFileFullName)] 
                                            = new CachedFileInfo(historyFileInfo, useNonFullPath: true);
                                    }
                                }
                            }
                        }   //foreach (var fileInfo in fileInfos)


                        if (Global.PersistentCacheDestAndHistoryFolders)
                        { 
                            if (
                                !Global.BidirectionalMirror
                                && Global.EnableMirror && isSrcPath && !forHistory
                                && updateDestDirPersistentCache
                            )
                            {
                                var destDirName = ConsoleWatch.GetOtherDirName(srcDirInfo.FullName, forHistory);
                                await ConsoleWatch.SaveFileInfoCache(destFileInfosToCache, destDirName, forHistory);
                            }


                            if (
                                Global.EnableHistory && isSrcPath && forHistory
                                && updateHistoryDirPersistentCache
                            )
                            {
                                var historyDirName = ConsoleWatch.GetOtherDirName(srcDirInfo.FullName, forHistory);
                                await ConsoleWatch.SaveFileInfoCache(historyFileInfosToCache, historyDirName, forHistory);
                            }
                        }
                    }   //if (Global.CacheDestAndHistoryFolders)
                }
                finally
                {
                    destDirCacheLock?.Dispose();
                    historyDirCacheLock?.Dispose();
                }


                //NB! loop the fileinfos only after dest and history fileinfo cache is populated
                foreach (var fileInfo in fileInfos)
                {
                    await yield.ReturnAsync(fileInfo);
                }


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


                    var subDirFileInfos = ProcessSubDirs(dirInfo, searchPattern, isSrcPath, forHistory, recursionLevel + 1);
                    await subDirFileInfos.ForEachAsync(async subDirFileInfo => 
                    {
                        await yield.ReturnAsync(subDirFileInfo);
                    });

                }   //foreach (var dirInfo in dirInfos)
#endif
            });   //return new AsyncEnumerable<int>(async yield => {
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
            //await Task.Run(() => 
            {
                //NB! using synchronous lock here since the MessageBox.Show and Console.WriteLine are synchronous
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
            }//)
            //.WaitAsync(Global.CancellationToken.Token);
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

    internal class BoolRef
    {
        public bool Value;
    }

    internal class FileInfoRef
    {
        public CachedFileInfo Value;
        public CancellationToken Token;

        [DebuggerStepThrough]
        public FileInfoRef(CachedFileInfo value, CancellationToken token)
        {
            Value = value;
            Token = token;
        }
    }

    internal class Context
    {
        public readonly IFileSystemEvent Event;
        public readonly CancellationToken Token;
        public readonly bool ForHistory;
        public readonly bool IsSrcPath;
        public readonly bool IsInitialScan;

        public CachedFileInfo FileInfo;
        public BoolRef FileInfoRefreshed;   //NB! need bool ref to share fileinfo refresh status between mirror and history contexts

        public CachedFileInfo OtherFileInfo;

        public DateTime Time
        {
            [DebuggerStepThrough]
            get
            {
                return Event?.DateTimeUtc ?? DateTime.UtcNow;
            }
        }

        [DebuggerStepThrough]
#pragma warning disable CA1068  //should take CancellationToken as the last parameter
        public Context(IFileSystemEvent eventObj, CancellationToken token, bool forHistory, bool isSrcPath, bool isInitialScan, BoolRef fileInfoRefreshedBoolRef)
#pragma warning restore CA1068
        {
            Event = eventObj;
            Token = token;
            ForHistory = forHistory;
            IsSrcPath = isSrcPath;
            IsInitialScan = isInitialScan;

            FileInfo = eventObj?.FileSystemInfo != null ? new CachedFileInfo(eventObj?.FileSystemInfo) : null;

            FileInfoRefreshed = fileInfoRefreshedBoolRef ?? new BoolRef();
            //FileInfo type is a file from directory scan and has stale file length. 
            //NB! if FileInfo is null then it is okay to set FileInfoRefreshed = true since if will be populated later with up-to-date information
            FileInfoRefreshed.Value = !(eventObj?.FileSystemInfo is FileInfo);    
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

        internal static DateTime PrevAlertTime;
        internal static string PrevAlertMessage;

        private static readonly ConcurrentDictionary<string, DateTime> BidirectionalSynchroniserSavedFileDates = new ConcurrentDictionary<string, DateTime>();
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
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.SrcPath));
        }

        public static bool IsMirrorDestPath(string fullNameInvariant)
        {
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.MirrorDestPath));
        }

        public static bool IsHistoryDestPath(string fullNameInvariant)
        {
            return Extensions.GetLongPath(fullNameInvariant).StartsWith(Extensions.GetLongPath(Global.HistoryDestPath));
        }

        public static string GetNonFullName(string fullName)
        {
            fullName = Extensions.GetLongPath(fullName);
            var fullNameInvariant = fullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);

            if (IsHistoryDestPath(fullNameInvariant))
            {
                return fullName.Substring(Extensions.GetLongPath(Global.HistoryDestPath).Length);
            }
            else if (IsMirrorDestPath(fullNameInvariant))
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

        public static string GetCacheDirName(string dirFullName, bool forHistory)
        {
            var fullNameInvariant = dirFullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var nonFullNameFolder = GetNonFullName(dirFullName);

            if (forHistory)
            {
                if (IsHistoryDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "History", nonFullNameFolder);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetCacheDirName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "Mirror", nonFullNameFolder);
                }
#if false
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.CachePath, "DestMirror", nonFullNameFolder);
                }
#endif
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetCacheDirName()");
                }
            }
        }   //public static string GetCacheDirName(string dirFullName, bool forHistory)

        public static string GetOtherDirName(string dirFullName, bool forHistory)
        {
            var fullNameInvariant = dirFullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var nonFullNameFolder = GetNonFullName(dirFullName);

            if (forHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.HistoryDestPath, nonFullNameFolder);
                }
                else
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherDirName()");
                }
            }
            else
            {
                if (IsMirrorDestPath(fullNameInvariant))
                {
                    return Path.Combine(Global.SrcPath, nonFullNameFolder);
                }
                else if (IsSrcPath(fullNameInvariant))
                {
                    return Path.Combine(Global.MirrorDestPath, nonFullNameFolder);
                }
                else 
                {
                    throw new ArgumentException("Unexpected path provided to GetOtherDirName()");
                }
            }
        }   //public static string GetOtherDirName(string dirFullName, bool forHistory)

        public static async Task<string> GetOtherFullName(FileInfo fileInfo, bool forHistory)
        {
            var fullNameInvariant = fileInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var nonFullName = GetNonFullName(fileInfo.FullName);

            if (forHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    var srcFileDate = fileInfo.LastWriteTimeUtc; //await GetFileTime(fileInfo);    //NB! here read the current file time, not file time at the event

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
                if (IsMirrorDestPath(fullNameInvariant))
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
        }   //public static async Task<string> GetOtherFullName(FileInfo fileInfo, bool forHistory)

        public static async Task<string> GetOtherFullName(Context context)
        {
            var fullNameInvariant = context.Event.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            var nonFullName = GetNonFullName(context.Event.FullName);

            if (context.ForHistory)
            {
                if (IsSrcPath(fullNameInvariant))
                {
                    var srcFileDate = await GetFileTime(context);    //NB! here read the current file time, not file time at the event

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
                if (IsMirrorDestPath(fullNameInvariant))
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
        }   //public static async Task<string> GetOtherFullName(Context context)

        public static async Task DeleteFile(FileInfoRef otherFileInfo, string otherFullName, Context context)
        {
            try
            {
                otherFullName = Extensions.GetLongPath(otherFullName);

                while (true)
                {
                    context.Token.ThrowIfCancellationRequested();

                    try
                    {
                        var backupFileInfo = new FileInfoRef(null, context.Token);
                        if (await GetFileExists(backupFileInfo, otherFullName + "~", isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory))
                        {
#pragma warning disable SEC0116 //Warning	SEC0116	Unvalidated file paths are passed to a file delete API, which can allow unauthorized file system operations (e.g. read, write, delete) to be performed on unintended server files.
                            await Extensions.FSOperation(() => File.Delete(otherFullName + "~"), context.Token);
#pragma warning restore SEC0116
                        }

                        //fileInfo?.Refresh();
                        if (await GetFileExists(otherFileInfo, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory))
                        {
                            await Extensions.FSOperation(() => File.Move(otherFullName, otherFullName + "~"), context.Token);
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

        public static async Task<Dictionary<string, CachedFileInfo>> ReadFileInfoCache(string dirName, bool forHistory)
        {
            Tuple<byte[], long> cacheDataTuple = null;
            if (Global.PersistentCacheDestAndHistoryFolders)
            {
                var cacheDirName = Extensions.GetLongPath(GetCacheDirName(dirName, forHistory));
                var cacheFileName = Path.Combine(cacheDirName, "dircache.dat");

                if (await Extensions.FSOperation(() => File.Exists(cacheFileName), Global.CancellationToken.Token))
                    cacheDataTuple = await FileExtensions.ReadAllBytesAsync(cacheFileName, Global.CancellationToken.Token);

            }   //if (Global.PersistentCacheDestAndHistoryFolders)


            if (cacheDataTuple?.Item1 != null)
            {
                try
                {
                    var cachedFileInfosDict = Extensions.DeserializeBinary<Dictionary<string, CachedFileInfo>>(cacheDataTuple.Item1);
                    return cachedFileInfosDict;
                }
                catch (SerializationException)
                {
                    //TODO: log error
                }
            }


            return null;
        }

        public static async Task SaveFileInfoCache(Dictionary<string, CachedFileInfo> dirCache, string dirName, bool forHistory)
        {
            if (Global.PersistentCacheDestAndHistoryFolders)
            {
                //BackgroundTaskManager.Run(async () =>
                //{
                    var cacheDirName = Extensions.GetLongPath(ConsoleWatch.GetCacheDirName(dirName, forHistory));
                    var cacheFileName = Path.Combine(cacheDirName, "dircache.dat");

                    if (!await Extensions.FSOperation(() => Directory.Exists(cacheDirName), Global.CancellationToken.Token))
                        await Extensions.FSOperation(() => Directory.CreateDirectory(cacheDirName), Global.CancellationToken.Token);

                    var serialisedData = Extensions.SerializeBinary(dirCache);
                    await FileExtensions.WriteAllBytesAsync(cacheFileName, serialisedData, createTempFileFirst: true, cancellationToken: Global.CancellationToken.Token);
                //});
            }
        }

        public static async Task RefreshFileInfo(Context context)
        {
            var fileInfo = context.Event.FileSystemInfo as FileInfo;
            if (fileInfo != null && !context.FileInfoRefreshed.Value)
            //var fileInfo = context.FileInfo;
            //if (!context.FileInfoRefreshed.Value)
            {
                context.FileInfoRefreshed.Value = true;

                await Extensions.FSOperation
                (
                    () => 
                    {
                        fileInfo.Refresh();    //https://stackoverflow.com/questions/7828132/getting-current-file-length-fileinfo-length-caching-and-stale-information
                        if (fileInfo.Exists)
                        { 
                            var dummyAttributes = fileInfo.Attributes;
                            var dymmyLength = fileInfo.Length;
                            var dymmyTime = fileInfo.LastWriteTimeUtc;
                        }
                    },
                    context.Token
                );


                context.FileInfo = new CachedFileInfo(fileInfo);


                //this method is called only on src files unless bidirectional mirroring is on. 
                //so actually there should be no need to update file cache here
                //but keeping this code here just in case
                if (
                    !context.IsSrcPath 
                    && Global.CacheDestAndHistoryFolders
                    && (!Global.BidirectionalMirror || context.ForHistory)
                )
                {
                    var fullName = Extensions.GetLongPath(context.Event.FullName);
                    Global.DestAndHistoryFileInfoCache[fullName] = context.FileInfo;


                    if (Global.PersistentCacheDestAndHistoryFolders)
                    {
                        var dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(fileInfo.DirectoryName));

                        using (await Global.PersistentCacheLocks.LockAsync(dirName, context.Token))
                        {
                            var cachedFileInfos = await ReadFileInfoCache(dirName, context.ForHistory);

                            cachedFileInfos[GetNonFullName(fullName)] = new CachedFileInfo(context.FileInfo, useNonFullPath: true);

                            await SaveFileInfoCache(cachedFileInfos, dirName, context.ForHistory);
                        }
                    }
                }
            }   //if (fileInfo != null && !context.FileInfoRefreshed.Value)
        }   //public static async Task RefreshFileInfo(Context context)

        public static async Task<bool> NeedsUpdate(Context context)
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
                if (context.FileInfo.Length != null)   //a file from directory scan
                {
                    //var fileLength = await GetFileSize(context);

                    var fileLength = context.FileInfo.Length.Value;      //NB! this info might be stale, but lets ignore that issue here
                    long maxFileSize = Math.Min(FileExtensions.MaxByteArraySize, Global.MaxFileSizeMB * (1024 * 1024));
                    if (maxFileSize > 0 && fileLength > maxFileSize)
                    {
                        await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : fileLength > maxFileSize : {fileLength} > {maxFileSize}", context);

                        return false;
                    } 
                }

                var synchroniserSaveDate = (Global.BidirectionalMirror && !context.ForHistory) ? GetBidirectionalSynchroniserSaveDate(context.Event.FullName) : DateTime.MinValue;
                var fileTime = context.Event.FileSystemInfo.LastWriteTimeUtc; //GetFileTime(fullName);

                if (
                    !Global.BidirectionalMirror   //no need to debounce BIDIRECTIONAL file save events when bidirectional save is disabled 
                    || context.ForHistory
                    || fileTime > synchroniserSaveDate.AddSeconds(3)     //NB! ignore if the file changed during 3 seconds after bidirectional save   //TODO!! config
                )
                {
                    var otherFullName = await GetOtherFullName(context);

                    bool considerDateAsNewer = false;
                    if (Global.DoNotCompareFileDate)
                    { 
                        considerDateAsNewer = true;
                    }
                    else
                    {
                        var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                        var otherFileTime = await GetFileTime(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                        context.OtherFileInfo = otherFileInfoRef.Value;

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
                                    var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                                    bool otherFileExists = await GetFileExists(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                                    context.OtherFileInfo = otherFileInfoRef.Value;

                                    if (!otherFileExists)
                                    {
                                        return true;
                                    }
                                    else
                                    {
                                        return false; 
                                    }
                                }
                                else
                                {
                                    return false;   //if the other file does not exist then the function returns true in date check
                                }
                            }
                            else    //if (Global.DoNotCompareFileSize)
                            {
                                var fileLength = await GetFileSize(context);

                                var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
                                var otherFileLength = await GetFileSize(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory);
                                context.OtherFileInfo = otherFileInfoRef.Value;

                                if (fileLength != otherFileLength)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false; 
                                }
                            }    //if (Global.DoNotCompareFileSize)
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

        public static async Task FileUpdated(Context context)
        {
            if (
                IsWatchedFile(context.Event.FullName, context.ForHistory, context.IsSrcPath)
                && (await NeedsUpdate(context))     //NB!
            )
            {
                var otherFullName = await GetOtherFullName(context);
                using (await Global.FileOperationLocks.LockAsync(context.Event.FullName, otherFullName, context.Token))
                {
                    using (await Global.FileOperationSemaphore.LockAsync())
                    {
                        long maxFileSize = Math.Min(FileExtensions.MaxByteArraySize, Global.MaxFileSizeMB * (1024 * 1024));
                        //TODO: consider destination disk free space here together with the file size already before reading the file

                        Tuple<byte[], long> fileDataTuple = null;
                        try
                        { 
                            fileDataTuple = await FileExtensions.ReadAllBytesAsync(Extensions.GetLongPath(context.Event.FullName), context.Token, maxFileSize);
                            if (fileDataTuple.Item1 == null)   //maximum length exceeded
                            {
                                await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : fileLength > maxFileSize : {fileDataTuple.Item2} > {maxFileSize}", context);

                                return; //TODO: log error?
                            }
                        }
                        catch (FileNotFoundException)   //file was removed by the time queue processing got to it
                        {
                            return;     
                        }

                        //save without transformations
                        await ConsoleWatch.SaveFileModifications(fileDataTuple.Item1, context);
                    }
                }
            }
        }

        private static async Task FileDeleted(Context context)
        {
            if (IsWatchedFile(context.Event.FullName, context.ForHistory, context.IsSrcPath))
            {
                await RefreshFileInfo(context);  //NB! verify that the file is still deleted

                if (!await GetFileExists(context))  //NB! verify that the file is still deleted
                {
                    var otherFullName = await GetOtherFullName(context);

                    var otherFileInfo = new FileInfoRef(null, context.Token);
                    await DeleteFile(otherFileInfo, otherFullName, context);
                }
                else    //NB! file appears to be recreated
                {
                    //await FileUpdated(fullName, isSrcPath, context);
                }
            }
        }

        private static Task<CachedFileInfo> GetFileInfo(Context context)
        {
            return GetFileInfo(context.Event.FullName, context.Token, context.IsSrcPath, context.ForHistory);
        }

        private static async Task<CachedFileInfo> GetFileInfo(string fullName, CancellationToken token, bool isSrcFile, bool forHistory)
        {
            fullName = Extensions.GetLongPath(fullName);


            CachedFileInfo result;
            bool useCache = false;
            if (
                !isSrcFile 
                && Global.CacheDestAndHistoryFolders
                && (!Global.BidirectionalMirror || forHistory)
            )
            {
                useCache = true;

                if (Global.DestAndHistoryFileInfoCache.TryGetValue(fullName, out result))
                    return result;
            }


            var fileInfo = await Extensions.FSOperation
            (
                () => 
                {
                    var fileInfo1 = new FileInfo(fullName);

                    //this will cause the actual filesystem call
                    if (fileInfo1.Exists)
                    {
                        var dummyAttributes = fileInfo1.Attributes;
                        var dymmyLength = fileInfo1.Length;
                        var dymmyTime = fileInfo1.LastWriteTimeUtc;
                    }

                    return fileInfo1;
                },
                token
            );

            result = new CachedFileInfo(fileInfo);


            if (useCache)
            { 
                Global.DestAndHistoryFileInfoCache[fullName] = result;


                if (Global.PersistentCacheDestAndHistoryFolders)
                {
                    var dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(fileInfo.DirectoryName)); 

                    using (await Global.PersistentCacheLocks.LockAsync(dirName, token))
                    { 
                        var cachedFileInfos = await ReadFileInfoCache(dirName, forHistory);

                        cachedFileInfos[GetNonFullName(fullName)] = new CachedFileInfo(result, useNonFullPath: true);

                        await SaveFileInfoCache(cachedFileInfos, dirName, forHistory);
                    }
                }
            }


            return result;
        }

        private static async Task<bool> GetIsFile(Context context)
        {
            if (context.FileInfo == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }

            return (context.FileInfo.Attributes & FileAttributes.Directory) == 0;
        }

        private static async Task<bool> GetFileExists(Context context)
        {
            if (context.FileInfo == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }

            return context.FileInfo.Exists.Value && (context.FileInfo.Attributes & FileAttributes.Directory) == 0;
        }

        private static async Task<bool> GetFileExists(FileInfoRef fileInfo, string fullName, bool isSrcFile, bool forHistory)
        {
            if (fileInfo.Value == null)
            {
                fileInfo.Value = await GetFileInfo(fullName, fileInfo.Token, isSrcFile, forHistory);
            }

            return fileInfo.Value.Exists.Value && (fileInfo.Value.Attributes & FileAttributes.Directory) == 0;
        }

        private static async Task<DateTime> GetFileTime(Context context)
        {
            if (context.FileInfo == null)
            {
                context.FileInfo = await GetFileInfo(context);

                if (!await GetFileExists(context))
                {
                    return DateTime.MinValue;
                }
            }

            return context.FileInfo.LastWriteTimeUtc;
        }

        private static async Task<DateTime> GetFileTime(FileInfoRef otherFileInfo, string otherFullName, bool isSrcFile, bool forHistory)
        {
            if (otherFileInfo.Value == null)
            {
                otherFileInfo.Value = await GetFileInfo(otherFullName, otherFileInfo.Token, isSrcFile, forHistory);

                if (!await GetFileExists(otherFileInfo, otherFullName, isSrcFile, forHistory))
                {
                    return DateTime.MinValue;
                }
            }

            return otherFileInfo.Value.LastWriteTimeUtc;
        }

        private static async Task<long> GetFileSize(Context context)
        {
            if (context.FileInfo.Length == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }
            else
            {
                await RefreshFileInfo(context);
            }

            if (!await GetFileExists(context))
            {
                return -1;
            }
            else
            { 
                return context.FileInfo.Length.Value;
            }
        }

        private static async Task<long> GetFileSize(FileInfoRef otherFileInfo, string otherFullName, bool isSrcFile, bool forHistory)
        {
            if (otherFileInfo.Value == null)
            {
                otherFileInfo.Value = await GetFileInfo(otherFullName, otherFileInfo.Token, isSrcFile, forHistory);

                if (!await GetFileExists(otherFileInfo, otherFullName, isSrcFile, forHistory))
                {
                    return -1;
                }
            }

            //NB! no RefreshFileInfo or GetFileExists calls here

            return otherFileInfo.Value.Length.Value;
        }

        private static bool IsWatchedFile(string fullName, bool forHistory, bool isSrcPath)
        {
            if (
                (!Global.EnableMirror && !forHistory)
                || (!Global.EnableHistory && forHistory)
                || (forHistory && !isSrcPath)
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

            var prevFileFSE = new DummyFileSystemEvent(fse.PreviousFileSystemInfo);
            var previousFullNameInvariant = prevFileFSE.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            bool previousPathIsSrcPath = IsSrcPath(previousFullNameInvariant);
            var previousContext = new Context(prevFileFSE, token, forHistory: false, isSrcPath: previousPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: null);

            var newFullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            bool newPathIsSrcPath = IsSrcPath(newFullNameInvariant);
            
            var fileInfoRefreshedBoolRef = new BoolRef();
            var newContexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: newPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef),
                new Context(fse, token, forHistory: true, isSrcPath: newPathIsSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
            };


            foreach (var newContext in newContexts)
            {
                try
                {
                    if (fse.IsFile)
                    {
                        var prevFileIsWatchedFile = IsWatchedFile(fse.PreviousFileSystemInfo.FullName, newContext.ForHistory, newPathIsSrcPath);
                        var newFileIsWatchedFile = IsWatchedFile(fse.FileSystemInfo.FullName, newContext.ForHistory, newPathIsSrcPath);

                        if (prevFileIsWatchedFile
                            || newFileIsWatchedFile)
                        {
                            await AddMessage(ConsoleColor.Cyan, $"[{(fse.IsFile ? "F" : "D")}][R]:{fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", newContext);

                            //NB! if file is renamed to cs~ or resx~ then that means there will be yet another write to same file, so lets skip this event here - NB! skip the event here, including delete event of the previous file
                            if (!fse.FileSystemInfo.FullName.EndsWith("~"))
                            {
                                //using (await Global.FileOperationLocks.LockAsync(rfse.FileSystemInfo.FullName, rfse.PreviousFileSystemInfo.FullName, context.Token))  //comment-out: prevent deadlock
                                {
                                    if (newFileIsWatchedFile)
                                    {
                                        await FileUpdated(newContext);
                                    }

                                    if (prevFileIsWatchedFile)
                                    {
                                        if (
                                            !newContext.ForHistory     //history files have a different name format than the original file names and would not cause a undefined behaviour
                                            && newFileIsWatchedFile     //both files were watched files
                                            && previousContext.IsSrcPath != newContext.IsSrcPath    
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
                                            if (
                                                newContext.ForHistory
                                                || (previousContext.IsSrcPath && Global.MirrorIgnoreSrcDeletions)
                                                || (!previousContext.IsSrcPath && Global.MirrorIgnoreDestDeletions)
                                            )
                                            {
                                                continue;
                                            }
                                            else
                                            { 
                                                await FileDeleted(previousContext);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        await AddMessage(ConsoleColor.Cyan, $"[{(fse.IsFile ? "F" : "D")}][R]:{fse.PreviousFileSystemInfo.FullName} > {fse.FileSystemInfo.FullName}", newContext);

                        //TODO trigger update / delete event for all files in new folder
                    }
                }
                catch (Exception ex)
                {
                    await WriteException(ex, newContext);
                }
            }
        }   //private static async Task OnRenamedAsync(IRenamedFileSystemEvent fse, CancellationToken token)

        internal static async Task OnRemovedAsync(IFileSystemEvent fse, CancellationToken token)
        {
            var fullNameInvariant = fse.FileSystemInfo.FullName.ToUpperInvariantOnWindows(Global.CaseSensitiveFilenames);
            bool isSrcPath = IsSrcPath(fullNameInvariant);

            var fileInfoRefreshedBoolRef = new BoolRef();
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef),
                new Context(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
            };

            foreach (var context in contexts)
            {
                context.FileInfo.Exists = false;

                try
                {
                    if (
                        context.ForHistory 
                        || (context.IsSrcPath && Global.MirrorIgnoreSrcDeletions)
                        || (!context.IsSrcPath && Global.MirrorIgnoreDestDeletions)
                    )
                    { 
                        continue;
                    }
                    else if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                        {
                            await AddMessage(ConsoleColor.Yellow, $"[{(fse.IsFile ? "F" : "D")}][-]:{fse.FileSystemInfo.FullName}", context);

                            using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                            {
                                await FileDeleted(context);
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
            bool isSrcPath = IsSrcPath(fullNameInvariant);

            var fileInfoRefreshedBoolRef = new BoolRef();
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef),
                new Context(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: isInitialScan, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
            };

            foreach (var context in contexts)
            {
                context.FileInfo.Exists = true;

                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                        {
                            if (!context.IsInitialScan)
                                await AddMessage(ConsoleColor.Green, $"[{(fse.IsFile ? "F" : "D")}][+]:{fse.FileSystemInfo.FullName}", context);

                            using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                            {
                                await FileUpdated(context);
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
            bool isSrcPath = IsSrcPath(fullNameInvariant);

            var fileInfoRefreshedBoolRef = new BoolRef();
            var contexts = new Context[] {
                new Context(fse, token, forHistory: false, isSrcPath: isSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef),
                new Context(fse, token, forHistory: true, isSrcPath: isSrcPath, isInitialScan: false, fileInfoRefreshedBoolRef: fileInfoRefreshedBoolRef)
            };

            foreach (var context in contexts)
            {
                context.FileInfo.Exists = true;

                try
                {
                    if (fse.IsFile)
                    {
                        if (IsWatchedFile(fse.FileSystemInfo.FullName, context.ForHistory, isSrcPath))
                        {
                            //check for file type only after checking IsWatchedFile first since file type checking might already be a slow operation
                            if (await GetIsFile(context))     //for some reason fse.IsFile is set even for folders
                            { 
                                await AddMessage(ConsoleColor.Gray, $"[{(fse.IsFile ? "F" : "D")}][T]:{fse.FileSystemInfo.FullName}", context);

                                using (await FileEventLocks.LockAsync(fse.FileSystemInfo.FullName, token))
                                {
                                    await FileUpdated(context);
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
            //await Task.Run(() =>
            {
                //NB! using synchronous lock here since the MessageBox.Show and Console.WriteLine are synchronous
                lock (Lock)
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
            }//)
            //.WaitAsync(context.Token);
        }

        public static async Task SaveFileModifications(byte[] fileData, Context context)
        {
            var otherFullName = await GetOtherFullName(context);


            var otherFileInfoRef = new FileInfoRef(context.OtherFileInfo, context.Token);
            var longOtherFullName = Extensions.GetLongPath(otherFullName);

            //NB! detect whether the file actually changed
            var otherFileDataTuple = 
                !Global.DoNotCompareFileContent 
                    && (await GetFileExists(otherFileInfoRef, otherFullName, isSrcFile: !context.IsSrcPath, forHistory: context.ForHistory))
                ? await FileExtensions.ReadAllBytesAsync(longOtherFullName, context.Token)    //TODO: optimisation: no need to read the bytes in case the file lengths are different
                : null;

            context.OtherFileInfo = otherFileInfoRef.Value;

            if (
                (
                    !Global.DoNotCompareFileContent
                    &&
                    (
                        (otherFileDataTuple?.Item1?.Length ?? -1) != fileData.Length
                        || !FileExtensions.BinaryEqual(otherFileDataTuple.Item1, fileData)
                    )
                )
                ||
                Global.DoNotCompareFileContent
            )
            {
                var minDiskFreeSpace = context.ForHistory ? Global.HistoryDestPathMinFreeSpace : (context.IsSrcPath ? Global.MirrorDestPathMinFreeSpace : Global.SrcPathMinFreeSpace);
                var actualFreeSpace = minDiskFreeSpace > 0 ? Extensions.CheckDiskSpace(otherFullName) : 0;
                if (minDiskFreeSpace > actualFreeSpace - fileData.Length)
                {
                    await AddMessage(ConsoleColor.Red, $"Error synchronising updates from file {context.Event.FullName} : minDiskFreeSpace > actualFreeSpace : {minDiskFreeSpace} > {actualFreeSpace}", context);

                    return;
                }


                //if (!context.ForHistory && context.OtherFileInfo.Exists != false)    //assume that in case of history files there is no point in making a back copy of the history file even if it exists at the destination
                //    await DeleteFile(otherFileInfoRef, otherFullName, context);


                var otherDirName = Extensions.GetDirPathWithTrailingSlash(Path.GetDirectoryName(otherFullName));
                var longOtherDirName = Extensions.GetLongPath(otherDirName);

                if (
                    !Global.CacheDestAndHistoryFolders
                    || !Global.CreatedFoldersCache.ContainsKey(longOtherDirName)
                )
                { 
                    if (!await Extensions.FSOperation(() => Directory.Exists(longOtherDirName), context.Token))
                        await Extensions.FSOperation(() => Directory.CreateDirectory(longOtherDirName), context.Token);                    

                    if (Global.CacheDestAndHistoryFolders)
                        Global.CreatedFoldersCache.TryAdd(longOtherDirName, true);
                }


                //invalidate file data in dirlist cache before file write
                if (Global.PersistentCacheDestAndHistoryFolders)
                {
                    using (await Global.PersistentCacheLocks.LockAsync(longOtherDirName, context.Token))
                    {
                        var cachedFileInfos = await ReadFileInfoCache(longOtherDirName, context.ForHistory);

                        cachedFileInfos.Remove(GetNonFullName(longOtherFullName));

                        await SaveFileInfoCache(cachedFileInfos, longOtherDirName, context.ForHistory);
                    }
                }


                var utcNowBeforeSave = DateTime.UtcNow;
                bool createTempFileFirst = !context.ForHistory && context.OtherFileInfo.Exists != false;
                await FileExtensions.WriteAllBytesAsync(longOtherFullName, fileData, createTempFileFirst: createTempFileFirst, cancellationToken: context.Token, writeBufferKB: Global.WriteBufferKB, bufferWriteDelayMs: Global.BufferWriteDelayMs);


                if (Global.BidirectionalMirror && !context.ForHistory)
                { 
                    var utcNowAfterSave = DateTime.UtcNow;  //NB! for bidirectional mirror compute now after saving the file
                    BidirectionalSynchroniserSavedFileDates[otherFullName] = utcNowAfterSave;
                }


                //the file info cache became obsolete for this file
                if (
                    context.IsSrcPath 
                    && Global.CacheDestAndHistoryFolders
                    && (!Global.BidirectionalMirror || context.ForHistory)
                )
                { 
                    CachedFileInfo fileInfo = null;
                    if (Global.DestAndHistoryFileInfoCache.TryGetValue(longOtherFullName, out fileInfo))
                    {
                        if (fileInfo.Exists.Value)
                        { 
                            fileInfo.LastWriteTimeUtc = utcNowBeforeSave;
                            fileInfo.Length = fileData.Length;
                        }
                        else
                        {
                            //Global.DestAndHistoryFileInfoCache.TryRemove(longOtherFullName, out fileInfo);
                            fileInfo = new CachedFileInfo(longOtherFullName, fileData.Length, utcNowBeforeSave);
                        }
                    }
                    else
                    {
                        fileInfo = new CachedFileInfo(longOtherFullName, fileData.Length, utcNowBeforeSave);

                        Global.DestAndHistoryFileInfoCache[longOtherFullName] = fileInfo;
                    }
                    

                    if (Global.PersistentCacheDestAndHistoryFolders)
                    {
                        using (await Global.PersistentCacheLocks.LockAsync(longOtherDirName, context.Token))
                        {
                            var cachedFileInfos = await ReadFileInfoCache(longOtherDirName, context.ForHistory);

                            cachedFileInfos[GetNonFullName(longOtherFullName)] = new CachedFileInfo(fileInfo, useNonFullPath: true);

                            await SaveFileInfoCache(cachedFileInfos, longOtherDirName, context.ForHistory);
                        }
                    }
                }


                await AddMessage(ConsoleColor.Magenta, $"Synchronised updates from file {context.Event.FullName}", context);


                if (Global.FileWriteDelayMs > 0)
                { 
#if !NOASYNC
                    await Task.Delay(Global.FileWriteDelayMs, context.Token);
#else
                    context.Token.WaitHandle.WaitOne(Global.FileWriteDelayMs);
#endif
                }
            }
            else if (false)     //TODO: config
            {
                //touch the file
                var now = DateTime.UtcNow;  //NB! compute common now for ConverterSavedFileDates

                try
                {
                    await Extensions.FSOperation(() => File.SetLastWriteTimeUtc(Extensions.GetLongPath(otherFullName), now), context.Token);
                }
                catch (Exception ex)
                {
                    await ConsoleWatch.WriteException(ex, context);
                }


                if (Global.BidirectionalMirror && !context.ForHistory)
                    BidirectionalSynchroniserSavedFileDates[otherFullName] = now;
            }
        }   //public static async Task SaveFileModifications(string fullName, byte[] fileData, byte[] originalData, Context context)

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


        public enum PROCESSINFOCLASS : int
        {
            ProcessIoPriority = 33
        }; 

        public enum PROCESSIOPRIORITY : int
        {
            PROCESSIOPRIORITY_UNKNOWN = -1,

            PROCESSIOPRIORITY_VERY_LOW = 0,
            PROCESSIOPRIORITY_LOW,
            PROCESSIOPRIORITY_NORMAL,
            PROCESSIOPRIORITY_HIGH
        };

        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtSetInformationProcess(IntPtr processHandle,
            PROCESSINFOCLASS processInformationClass, 
            [In] ref int processInformation,
            uint processInformationLength);

        public static bool NT_SUCCESS(int Status)
        {
            return (Status >= 0);
        }

        public static bool SetIOPriority(IntPtr processHandle, PROCESSIOPRIORITY ioPriorityIn)
        {
            //PROCESSINFOCLASS.ProcessIoPriority is actually only available only on XPSP3, Server2003, Vista or newer: http://blogs.norman.com/2011/security-research/ntqueryinformationprocess-ntsetinformationprocess-cheat-sheet
            try
            { 
                int ioPriority = (int)ioPriorityIn;
                int result = NtSetInformationProcess(processHandle, PROCESSINFOCLASS.ProcessIoPriority, ref ioPriority, sizeof(int));
                return NT_SUCCESS(result);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }   //internal static class WindowsDllImport 
}
