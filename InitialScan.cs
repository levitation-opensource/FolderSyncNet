﻿//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using myoddweb.directorywatcher.interfaces;
using Nito.AspNetBackgroundTasks;
using Nito.AsyncEx;

//using DirectoryInfo = FolderSyncNetSource.DirectoryInfo;

namespace FolderSync
{
    class DummyFileSystemEvent //: IFileSystemEvent
    {
        [DebuggerStepThrough]
        public DummyFileSystemEvent(FileSystemInfo fileSystemInfo)  //this constructor is needed for interface compatibility
        {
            FileSystemInfo = fileSystemInfo;
            FullName = fileSystemInfo.FullName;
            Name = fileSystemInfo.Name;
            Action = EventAction.Added;
            Error = EventError.None;
            DateTimeUtc = DateTime.UtcNow;
            IsFile = true;
        }

        [DebuggerStepThrough]
        public DummyFileSystemEvent(IFileSystemEvent iFileSystemEvent)
        {
            FileSystemInfo = iFileSystemEvent.FileSystemInfo;
            FullName = iFileSystemEvent.FileSystemInfo.FullName;
            Name = iFileSystemEvent.FileSystemInfo.Name;
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

    internal partial class Program
    {
        private static readonly AsyncCountdownEvent InitialSyncCountdownEvent = new AsyncCountdownEvent(1);

        private static Dictionary<string, FileInfo> HistorySrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorSrcPrevFileInfos = new Dictionary<string, FileInfo>();
        private static Dictionary<string, FileInfo> MirrorDestPrevFileInfos = new Dictionary<string, FileInfo>();

        private static async Task ScanFolders(WatcherContext initialSyncMessageContext)
        {
            bool keepFileInfosForLaterPolling = Global.UsePolling;

            //1. Do initial history synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableHistory)
            {
                var historySrcPrevFileInfosRef = new FileInfosRef(HistorySrcPrevFileInfos);
                await ScanFolder(historySrcPrevFileInfosRef, Global.SrcPath, "*." + (Global.HistoryWatchedExtension.Count == 1 ? Global.HistoryWatchedExtension.Single() : "*"), isSrcPath: true, forHistory: true, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, initialSyncMessageContext: initialSyncMessageContext);
                HistorySrcPrevFileInfos = historySrcPrevFileInfosRef.Value;
            }

            //2. Do initial mirror synchronisation from src to dest folder   //TODO: config for enabling and ordering of this operation
            if (Global.EnableMirror)
            {
                var mirrorSrcPrevFileInfosRef = new FileInfosRef(MirrorSrcPrevFileInfos);
                await ScanFolder(mirrorSrcPrevFileInfosRef, Global.SrcPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), isSrcPath: true, forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, initialSyncMessageContext: initialSyncMessageContext);
                MirrorSrcPrevFileInfos = mirrorSrcPrevFileInfosRef.Value;
            }

            if (Global.BidirectionalMirror)
            {
                //3. Do initial mirror synchronisation from dest to src folder   //TODO: config for enabling and ordering of this operation
                var mirrorDestPrevFileInfosRef = new FileInfosRef(MirrorDestPrevFileInfos);
                await ScanFolder(mirrorDestPrevFileInfosRef, Global.MirrorDestPath, "*." + (Global.MirrorWatchedExtension.Count == 1 ? Global.MirrorWatchedExtension.Single() : "*"), isSrcPath: false, forHistory: false, keepFileInfosForLaterPolling: keepFileInfosForLaterPolling, initialSyncMessageContext: initialSyncMessageContext);
                MirrorDestPrevFileInfos = mirrorDestPrevFileInfosRef.Value;
            }

            if (initialSyncMessageContext?.IsInitialScan == true)
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

        private static async Task ScanFolder(FileInfosRef PrevFileInfos, string path, string extension, bool isSrcPath, bool forHistory, bool keepFileInfosForLaterPolling, WatcherContext initialSyncMessageContext)
        {
            var NewFileInfos = !Global.MirrorIgnoreSrcDeletions ? new Dictionary<string, FileInfo>() : null;

            var fileInfos = ProcessSubDirs(new FolderSyncNetSource.DirectoryInfo(Extensions.GetLongPath(path)), extension, isSrcPath, forHistory, initialSyncMessageContext: initialSyncMessageContext);

            await fileInfos.ForEachAsync
            (
                fileInfo =>
                {
                    if (!Global.MirrorIgnoreSrcDeletions)
                        NewFileInfos.Add(fileInfo.FullName, fileInfo);

                    FileInfo prevFileInfo;
                    //FolderSyncNetSource.FileInfo prevFileInfo;
                    if (!PrevFileInfos.Value.TryGetValue(fileInfo.FullName, out prevFileInfo))
                    {
                        if (Global.MirrorIgnoreSrcDeletions)
                            PrevFileInfos.Value.Add(fileInfo.FullName, fileInfo);

                        if (initialSyncMessageContext?.IsInitialScan == true)
                            InitialSyncCountdownEvent.AddCount();

                        BackgroundTaskManager.Run(async () =>
                        {
                            await ConsoleWatch.OnAddedAsync
                            (
                                new DummyFileSystemEvent(fileInfo),
                                Global.CancellationToken.Token,
                                initialSyncMessageContext?.IsInitialScan == true
                            );

                            if (initialSyncMessageContext?.IsInitialScan == true)
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
                                    Global.CancellationToken.Token,
                                    initialSyncMessageContext?.IsInitialScan == true
                                );
                            });
                        }
                    }   //if (!PrevAddedFileInfos.TryGetValue(fileInfo.FullName, out prevFileInfo))
                },
                Global.CancellationToken.Token
            );   //await fileInfos.ForEachAsync(async fileInfo => {

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
                                Global.CancellationToken.Token,
                                initialSyncMessageContext?.IsInitialScan == true
                            );
                        });
                    }
                }

                PrevFileInfos.Value = NewFileInfos;

            }   //if (!Global.MirrorIgnoreSrcDeletions)

        }   //private static async Task ScanFolder(string path, string extension, bool forHistory, bool keepFileInfosForLaterPolling)

        private static IAsyncEnumerable<FileInfo> ProcessSubDirs(FolderSyncNetSource.DirectoryInfo srcDirInfo, string searchPattern, bool isSrcPath, bool forHistory, int recursionLevel = 0, WatcherContext initialSyncMessageContext = null)
        {
            return new AsyncEnumerable<FileInfo>(async yield => {
                //return new AsyncEnumerable<FolderSyncNetSource.FileInfo>(async yield => {

                if (Global.CancellationToken.IsCancellationRequested)
                    return;

                if (Global.LogInitialScan && initialSyncMessageContext?.IsInitialScan == true)
                    await ConsoleWatch.AddMessage(ConsoleColor.Blue, "Scanning folder " + Extensions.GetLongPath(srcDirInfo.FullName), initialSyncMessageContext);


                if (Global.DirlistReadDelayMs > 0)
                {
#if !NOASYNC
                    await Task.Delay(Global.DirlistReadDelayMs, Global.CancellationToken.Token);     //TODO: config file?
#else
                    Global.CancellationToken.Token.WaitHandle.WaitOne(Global.DirlistReadDelayMs);
#endif
                }



#if false //this built-in functio will throw IOException in case some subfolder is an invalid reparse point
                return new DirectoryInfo(sourceDir)
                    .GetFiles(searchPattern, SearchOption.AllDirectories);
#else

                //Directory.GetFileSystemEntries would not help here since it returns only strings, not FileInfos

                //TODO: under Windows10 use https://github.com/ljw1004/uwp-desktop for true async dirlists


                var destFileInfosDict = new Dictionary<string, CachedFileInfo>();
                var destOrHistoryFileInfosTask = Task.CompletedTask;

                var historyFileInfosDict = new Dictionary<string, CachedFileInfo>();
                //var historyFileInfosTask = Task.CompletedTask;

                AsyncLockQueueDictionary<string>.LockDictReleaser destOrHistoryDirCacheLock = null;
                //AsyncLockQueueDictionary<string>.LockDictReleaser historyDirCacheLock = null;

                FileInfo[] fileInfos = null;
                //DirectoryInfo[] dirInfos = null;
                //FolderSyncNetSource.FileInfo[] fileInfos = null;
                FolderSyncNetSource.DirectoryInfo[] dirInfos = null;

                bool updateDestDirPersistentCache = false;
                bool updateHistoryDirPersistentCache = false;

                try     //finally for destDirCacheLock and historyDirCacheLock
                {
                    if (Global.CacheDestAndHistoryFolders/* || Global.PersistentCacheDestAndHistoryFolders*/)
                    {
                        if (
                            isSrcPath
                            && (
                                (
                                    !forHistory
                                    && !Global.BidirectionalMirror
                                    && Global.EnableMirror 
                                )
                                ||
                                (
                                    forHistory
                                    && Global.EnableHistory 
                                )
                            )
                        )
                        {
                            destOrHistoryFileInfosTask = Task.Run(async () =>
                            {
                                try
                                {
                                    var destOrHistoryDirName = Extensions.GetLongPath(ConsoleWatch.GetOtherDirName(srcDirInfo.FullName, forHistory));
                                    destOrHistoryDirCacheLock = await Global.PersistentCacheLocks.LockAsync(destOrHistoryDirName.ToUpperInvariantOnWindows(), Global.CancellationToken.Token);
                                    var destOrHistoryFileInfos = (await ConsoleWatch.ReadFileInfoCache(destOrHistoryDirName, forHistory))?.Values.ToList();

                                    if (destOrHistoryFileInfos == null)
                                    {
                                        //TODO!!! if cachedFileInfos == null then scan entire folder and create cached file infos file

                                        if (!forHistory)
                                            updateDestDirPersistentCache = true;

                                        if (forHistory)
                                            updateHistoryDirPersistentCache = true;


                                        var destOrHistoryDirInfo = new FolderSyncNetSource.DirectoryInfo(destOrHistoryDirName);

                                        if (await Extensions.FSOperation
                                        (
                                            cancellationAndTimeoutToken => Directory.Exists(destOrHistoryDirName),
                                            destOrHistoryDirName,
                                            Global.CancellationToken.Token
                                        ))
                                        {
                                            destOrHistoryFileInfos = (await Extensions.DirListOperation
                                            (
                                                cancellationAndTimeoutToken => destOrHistoryDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly, cancellationAndTimeoutToken),
                                                destOrHistoryDirInfo.FullName,
                                                Global.RetryCountOnEmptyDirlist,
                                                Global.CancellationToken.Token
                                            ))
                                            .Select(x => new CachedFileInfo(x))
                                            .ToList();
                                        }
                                        else
                                        {
                                            destOrHistoryFileInfos = new List<CachedFileInfo>();
                                        }
                                    }
                                    else   //if (destFileInfos == null)
                                    {
                                        foreach (var fileInfo in destOrHistoryFileInfos)
                                        {
                                            fileInfo.FullName = FolderSyncNetSource.Path.Combine(Global.MirrorDestPath, fileInfo.FullName);
                                        }
                                    }


                                    if (
                                        !Global.CreatedFoldersCache.ContainsKey(destOrHistoryDirName.ToUpperInvariantOnWindows())
                                        && destOrHistoryFileInfos.Any(x => x.Exists == true)
                                    )
                                    {
                                        Global.CreatedFoldersCache.TryAdd(destOrHistoryDirName.ToUpperInvariantOnWindows(), true);
                                    }


                                    //Google Drive can have multiple files with same name
                                    foreach (var fileInfo in destOrHistoryFileInfos)
                                    {
                                        if (Global.CancellationToken.IsCancellationRequested)
                                            return;

                                        var longFullName = Extensions.GetLongPath(fileInfo.FullName);

                                        CachedFileInfo prevFileInfo;
                                        if (destFileInfosDict.TryGetValue(longFullName.ToUpperInvariantOnWindows(), out prevFileInfo))
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

                                        if (!forHistory)
                                            destFileInfosDict[longFullName.ToUpperInvariantOnWindows()] = fileInfo;
                                        else
                                            historyFileInfosDict[longFullName.ToUpperInvariantOnWindows()] = fileInfo;
                                    }
                                }
                                catch (Exception ex) when (
                                    ex is DirectoryNotFoundException 
                                    || ex is UnauthorizedAccessException
                                    || ex is ArgumentException  //illegal characters in path
                                )
                                {
                                    //ignore the error

                                    //UnauthorizedAccessException can also occur when a folder was just created, but it can still be ignored here since then file add handler will take care of caching that folder
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
                                cancellationAndTimeoutToken => srcDirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly, cancellationAndTimeoutToken),
                                srcDirInfo.FullName,
                                Global.RetryCountOnEmptyDirlist,
                                Global.CancellationToken.Token
                            );
                        }
                        catch (Exception ex) when (
                            ex is DirectoryNotFoundException 
                            || ex is UnauthorizedAccessException
                            || ex is ArgumentException  //illegal characters in path
                            || ex is TimeoutException
                        )
                        {
                            fileInfos = Array.Empty<FileInfo>();
                            //fileInfos = Array.Empty<FolderSyncNetSource.FileInfo>();

                            //UnauthorizedAccessException can also occur when a folder was just created, but it can still be ignored here since then file add handler will take care of that folder
                        }
                    })
                    .WaitAsync(Global.CancellationToken.Token);


                    var dirInfosTask = Task.Run(async () =>
                    {
                        try
                        {
                            dirInfos = await Extensions.DirListOperation
                            (
                                cancellationAndTimeoutToken => srcDirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly, cancellationAndTimeoutToken),
                                srcDirInfo.FullName,
                                Global.RetryCountOnEmptyDirlist,
                                Global.CancellationToken.Token
                            );
                        }
                        catch (Exception ex) when (
                            ex is DirectoryNotFoundException 
                            || ex is UnauthorizedAccessException
                            || ex is ArgumentException  //illegal characters in path
                            || ex is TimeoutException
                        )
                        {
                            //dirInfos = Array.Empty<DirectoryInfo>();
                            dirInfos = Array.Empty<FolderSyncNetSource.DirectoryInfo>();

                            //UnauthorizedAccessException can also occur when a folder was just created, but it can still be ignored here since then file add handler will take care of that folder
                        }
                    })
                    .WaitAsync(Global.CancellationToken.Token);


                    await Task.WhenAll(destOrHistoryFileInfosTask, /*historyFileInfosTask, */fileInfosTask, dirInfosTask);


                    if (Global.CacheDestAndHistoryFolders/* || Global.PersistentCacheDestAndHistoryFolders*/)
                    {
                        var destFileInfosToCache = new Dictionary<string, CachedFileInfo>();
                        var historyFileInfosToCache = new Dictionary<string, CachedFileInfo>();

                        foreach (var fileInfo in fileInfos)
                        {
                            if (Global.CancellationToken.IsCancellationRequested)
                                return;

                            if (
                                !Global.BidirectionalMirror
                                && Global.EnableMirror && isSrcPath && !forHistory
                            )
                            {
                                var destFileFullName = await ConsoleWatch.GetOtherFullName(fileInfo, forHistory);
                                destFileFullName = Extensions.GetLongPath(destFileFullName);

                                CachedFileInfo destFileInfo;
                                if (destFileInfosDict.TryGetValue(destFileFullName.ToUpperInvariantOnWindows(), out destFileInfo))
                                {
                                    Global.DestAndHistoryFileInfoCache[destFileFullName.ToUpperInvariantOnWindows()] = destFileInfo;

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
                                if (historyFileInfosDict.TryGetValue(historyFileFullName.ToUpperInvariantOnWindows(), out historyFileInfo))     //TODO!!! historyFileInfosDict is never written to?
                                {
                                    Global.DestAndHistoryFileInfoCache[historyFileFullName.ToUpperInvariantOnWindows()] = historyFileInfo;

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
                    destOrHistoryDirCacheLock?.Dispose();
                    //historyDirCacheLock?.Dispose();
                }


                //NB! loop the fileinfos only after dest and history fileinfo cache is populated
                foreach (var fileInfo in fileInfos)
                {
                    if (Global.CancellationToken.IsCancellationRequested)
                        return;

                    await yield.ReturnAsync(fileInfo);
                }


                foreach (var dirInfo in dirInfos)
                {
                    if (Global.CancellationToken.IsCancellationRequested)
                        return;

                    //TODO: option to follow reparse points
                    if (
                        (int)dirInfo.Attributes != -1   //some unusual attributes found in some folders of network drives
                        && (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint
                    )
                    {
                        continue;
                    }


                    var nonFullNameInvariantWithLeadingSlash = DirectorySeparatorChar + Extensions.GetDirPathWithTrailingSlash(ConsoleWatch.GetNonFullName(dirInfo.FullName.ToUpperInvariantOnWindows()));
                    if (!forHistory)
                    {
                        if (
                            //Global.MirrorIgnorePathsStartingWith.Any(x => nonFullNameInvariantWithLeadingSlash.StartsWith(x))
                            //|| Global.MirrorIgnorePathsContaining.Any(x => nonFullNameInvariantWithLeadingSlash.Contains(x))
                            //|| Global.MirrorIgnorePathsEndingWith.Any(x => nonFullNameInvariantWithLeadingSlash.EndsWith(x))
                            Global.MirrorIgnorePathsContainingACHasAny  //needed to avoid exceptions
                            && Global.MirrorIgnorePathsContainingAC.ParseText(NullChar + nonFullNameInvariantWithLeadingSlash/* + NullChar*/).Any() //NB! no NullChar appended to end since it is dir path not complete file path
                        )
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (
                            //Global.HistoryIgnorePathsStartingWith.Any(x => nonFullNameInvariantWithLeadingSlash.StartsWith(x))
                            //|| Global.HistoryIgnorePathsContaining.Any(x => nonFullNameInvariantWithLeadingSlash.Contains(x))
                            //|| Global.HistoryIgnorePathsEndingWith.Any(x => nonFullNameInvariantWithLeadingSlash.EndsWith(x))
                            Global.HistoryIgnorePathsContainingACHasAny  //needed to avoid exceptions
                            && Global.HistoryIgnorePathsContainingAC.ParseText(NullChar + nonFullNameInvariantWithLeadingSlash/* + NullChar*/).Any() //NB! no NullChar appended to end since it is dir path not complete file path
                        )
                        {
                            continue;
                        }
                    }


                    var subDirFileInfos = ProcessSubDirs(dirInfo, searchPattern, isSrcPath, forHistory, recursionLevel + 1, initialSyncMessageContext: initialSyncMessageContext);
                    await subDirFileInfos.ForEachAsync
                    (
                        async subDirFileInfo =>
                        {
                            await yield.ReturnAsync(subDirFileInfo);
                        },
                        Global.CancellationToken.Token
                    );

                }   //foreach (var dirInfo in dirInfos)
#endif
            });   //return new AsyncEnumerable<int>(async yield => {
        }   //private static IEnumerable<FileInfo> ProcessSubDirs(DirectoryInfo srcDirInfo, string searchPattern, bool forHistory, int recursionLevel = 0)

    }
}
