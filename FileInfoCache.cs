//
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
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FolderSync
{
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

    internal partial class ConsoleWatch
    {
        public static async Task<Dictionary<string, CachedFileInfo>> ReadFileInfoCache(string dirName, bool forHistory)
        {
            Tuple<byte[], long> cacheDataTuple = null;
            if (Global.PersistentCacheDestAndHistoryFolders)
            {
                var cacheDirName = Extensions.GetLongPath(GetCacheDirName(dirName, forHistory));
                var cacheFileName = Path.Combine(cacheDirName, "dircache.dat");

                if (await Extensions.FSOperation
                (
                    cancellationAndTimeoutToken => File.Exists(cacheFileName),
                    cacheFileName,
                    Global.CancellationToken.Token,
                    timeout: 0,     //NB!
                    suppressLongRunningOperationMessage: true     //NB!
                ))
                {
                    cacheDataTuple = await FileExtensions.ReadAllBytesAsync
                    (
                        cacheFileName,
                        /*allowVSS: */false,
                        Global.CancellationToken.Token,
                        timeout: 0,     //NB!
                        suppressLongRunningOperationMessage: true     //NB!
                    );
                }
            }   //if (Global.PersistentCacheDestAndHistoryFolders)


            if (cacheDataTuple?.Item1 != null)
            {
                try
                {
                    var cachedFileInfosDict = Extensions.DeserializeBinary<Dictionary<string, CachedFileInfo>>(cacheDataTuple.Item1);

                    //TODO: add parent folders to paths if they were removed

                    var cachedFileInfosDictUpperKeysOnWindows = cachedFileInfosDict
                        .ToDictionary
                        (
                            kvp => kvp.Key.ToUpperInvariantOnWindows(),     //backwards compatibility - old cache files might contain non-uppercase keys
                            kvp => kvp.Value
                        );

                    return cachedFileInfosDictUpperKeysOnWindows;
                }
                catch (SerializationException)
                {
                    //TODO: log error
                }
            }   //if (cacheDataTuple?.Item1 != null)


            return null;

        }   //public static async Task<Dictionary<string, CachedFileInfo>> ReadFileInfoCache(string dirName, bool forHistory)

        public static async Task SaveFileInfoCache(Dictionary<string, CachedFileInfo> dirCache, string dirName, bool forHistory)
        {
            if (Global.PersistentCacheDestAndHistoryFolders)
            {
                //BackgroundTaskManager.Run(async () =>
                //{
                var cacheDirName = Extensions.GetLongPath(ConsoleWatch.GetCacheDirName(dirName, forHistory));
                var cacheFileName = Path.Combine(cacheDirName, "dircache.dat");

                if (!await Extensions.FSOperation
                (
                    cancellationAndTimeoutToken => Directory.Exists(cacheDirName),
                    cacheDirName,
                    Global.CancellationToken.Token,
                    timeout: 0,     //NB!
                    suppressLongRunningOperationMessage: true     //NB!
                ))
                {
                    await Extensions.FSOperation
                    (
                        cancellationAndTimeoutToken => Directory.CreateDirectory(cacheDirName),
                        cacheDirName,
                        Global.CancellationToken.Token,
                        timeout: 0,     //NB!
                        suppressLongRunningOperationMessage: true     //NB!
                    );
                }

                //TODO: remove parent folders from paths

                var serialisedData = Extensions.SerializeBinary(dirCache);
                await FileExtensions.WriteAllBytesAsync
                (
                    cacheFileName,
                    serialisedData,
                    createTempFileFirst: true,
                    cancellationToken: Global.CancellationToken.Token,
                    retryCount: 5,  //TODO: config
                    timeout: 0,     //NB!
                    suppressLongRunningOperationMessage: true     //NB!
                );
                //});
            }
        }

        public static async Task RefreshFileInfo(WatcherContext context)
        {
            var fileInfo = context.Event.FileSystemInfo as FileInfo;
            //var fileInfo = context.Event.FileSystemInfo.AsFileInfo(); // as FileInfo;

            if (fileInfo != null && !context.FileInfoRefreshed.Value)
            //var fileInfo = context.FileInfo;
            //if (!context.FileInfoRefreshed.Value)
            {
                context.FileInfoRefreshed.Value = true;

                await Extensions.FSOperation
                (
                    cancellationAndTimeoutToken =>
                    {
                        fileInfo.Refresh();    //https://stackoverflow.com/questions/7828132/getting-current-file-length-fileinfo-length-caching-and-stale-information
                        if (fileInfo.Exists)
                        {
                            var dummyAttributes = fileInfo.Attributes;
                            var dymmyLength = fileInfo.Length;
                            var dymmyTime = fileInfo.LastWriteTimeUtc;
                        }
                    },
                    fileInfo.FullName,
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
                    //var fullName = Extensions.GetLongPath(context.Event.FullName);
                    //Global.DestAndHistoryFileInfoCache[fullName.ToUpperInvariantOnWindows()] = context.FileInfo;


#if true
                    await SaveFileInfo(context.Event.FullName, context.FileInfo, context.ForHistory, fileInfo.DirectoryName, cancellationToken: context.Token);
#else
                    if (Global.PersistentCacheDestAndHistoryFolders)
                    {
                        var dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(fileInfo.DirectoryName));

                        using (await Global.PersistentCacheLocks.LockAsync(dirName.ToUpperInvariantOnWindows(), context.Token))
                        {
                            var cachedFileInfos = await ReadFileInfoCache(dirName, context.ForHistory);

                            if (cachedFileInfos != null)    //TODO: if cachedFileInfos == null then scan entire folder and create cached file infos file
                            {
                                cachedFileInfos[GetNonFullName(fullName).ToUpperInvariantOnWindows()] = new CachedFileInfo(context.FileInfo, useNonFullPath: true);

                                await SaveFileInfoCache(cachedFileInfos, dirName, context.ForHistory);
                            }
                        }
                    }
#endif
                }
            }   //if (fileInfo != null && !context.FileInfoRefreshed.Value)
        }   //public static async Task RefreshFileInfo(Context context)   

        internal static async Task SaveFileInfo(string fileName, CachedFileInfo fileInfo, bool forHistory, string dirName, CancellationToken cancellationToken)
        {
            //TODO debounce the persistent cache saves and save entire dir cache together, not reading the cache file again each time

            //handle fileInfo == null as file deletion
            if (fileInfo == null)
            {
#if false
                var cachedFileInfos = await ReadFileInfoCache(dirName);

                CachedFileInfo dummy;
                var removed = Global.FileInfoCache.TryRemove(fileName.ToUpperInvariantOnWindows(), out dummy);

                if (removed)
                { 
                    //ConsoleWatch.SaveFileInfoCache(Dictionary<string, CachedFileInfo> dirCache, string dirName);   //TODO!
                }
#else
                var fullName = Extensions.GetLongPath(fileName);

                CachedFileInfo dummy;
                var removed1 = Global.DestAndHistoryFileInfoCache.TryRemove(fileName.ToUpperInvariantOnWindows(), out dummy);


                if (Global.PersistentCacheDestAndHistoryFolders && removed1)
                {
                    if (dirName == null)
                        dirName = FolderSyncNetSource.Path.GetDirectoryName(fileName);

                    dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(dirName));


                    using (await Global.PersistentCacheLocks.LockAsync(dirName.ToUpperInvariantOnWindows(), cancellationToken))
                    {
                        var cachedFileInfos = await ReadFileInfoCache(dirName, forHistory);

                        if (cachedFileInfos != null)    //TODO: if cachedFileInfos == null then scan entire folder and create cached file infos file
                        {
                            var removed2 = cachedFileInfos.Remove(GetNonFullName(fullName).ToUpperInvariantOnWindows());

                            if (removed2)
                            {
                                await SaveFileInfoCache(cachedFileInfos, dirName, forHistory);
                            }
                        }
                    }
                }
#endif
            }
            else    //if (fileInfo == null)
            {
                var fullName = Extensions.GetLongPath(fileName);
                Global.DestAndHistoryFileInfoCache[fullName.ToUpperInvariantOnWindows()] = fileInfo;


                if (Global.PersistentCacheDestAndHistoryFolders)
                {
                    if (dirName == null)
                        dirName = FolderSyncNetSource.Path.GetDirectoryName(fileName);

                    dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(dirName));


                    using (await Global.PersistentCacheLocks.LockAsync(dirName.ToUpperInvariantOnWindows(), cancellationToken))
                    {
                        var cachedFileInfos = await ReadFileInfoCache(dirName, forHistory);

                        if (cachedFileInfos != null)    //TODO: if cachedFileInfos == null then scan entire folder and create cached file infos file
                        {
                            cachedFileInfos[GetNonFullName(fullName).ToUpperInvariantOnWindows()] = new CachedFileInfo(fileInfo, useNonFullPath: true);

                            await SaveFileInfoCache(cachedFileInfos, dirName, forHistory);
                        }
                    }
                }
            }    //if (fileInfo == null)
        }   //internal static async Task SaveFileInfo(string fileName, CachedFileInfo fileInfo, CancellationToken cancellationToken = default(CancellationToken)) 

        private static Task<CachedFileInfo> GetFileInfo(WatcherContext context)
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

                if (Global.DestAndHistoryFileInfoCache.TryGetValue(fullName.ToUpperInvariantOnWindows(), out result))
                    return result;
            }


            var fileInfo = await Extensions.FSOperation
            (
                cancellationAndTimeoutToken =>
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
                fullName,
                token
            );

            result = new CachedFileInfo(fileInfo);


            if (useCache)
            {
                Global.DestAndHistoryFileInfoCache[fullName.ToUpperInvariantOnWindows()] = result;


                if (Global.PersistentCacheDestAndHistoryFolders)
                {
                    var dirName = Extensions.GetLongPath(Extensions.GetDirPathWithTrailingSlash(fileInfo.DirectoryName));

                    using (await Global.PersistentCacheLocks.LockAsync(dirName.ToUpperInvariantOnWindows(), token))
                    {
                        var cachedFileInfos = await ReadFileInfoCache(dirName, forHistory);

                        if (cachedFileInfos != null)    //TODO: if cachedFileInfos == null then scan entire folder and create cached file infos file
                        {
                            cachedFileInfos[GetNonFullName(fullName).ToUpperInvariantOnWindows()] = new CachedFileInfo(result, useNonFullPath: true);

                            await SaveFileInfoCache(cachedFileInfos, dirName, forHistory);
                        }
                    }
                }
            }


            return result;
        }

        private static async Task<bool> GetIsFile(WatcherContext context)
        {
            if (context.FileInfo == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }

            return !context.FileInfo.Attributes.HasFlag(FileAttributes.Directory);
        }

        private static async Task<bool> GetFileExists(WatcherContext context)
        {
            if (context.FileInfo == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }

            return context.FileInfo.Exists.Value 
                    && !context.FileInfo.Attributes.HasFlag(FileAttributes.Directory);
        }

        private static async Task<bool> GetFileExists(FileInfoRef fileInfo, string fullName, bool isSrcFile, bool forHistory)
        {
            if (fileInfo.Value == null)
            {
                fileInfo.Value = await GetFileInfo(fullName, fileInfo.Token, isSrcFile, forHistory);
            }

            return fileInfo.Value.Exists.Value 
                    && !fileInfo.Value.Attributes.HasFlag(FileAttributes.Directory);
        }

        private static async Task<DateTime> GetFileTime(WatcherContext context)
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

        private static async Task<long> GetFileSize(WatcherContext context)
        {
            if (context.FileInfo.Length == null)
            {
                context.FileInfo = await GetFileInfo(context);
            }
#if false
            else
            {
                await RefreshFileInfo(context);
            }
#endif

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

            if (!otherFileInfo.Value.Exists.Value)
                return -1;
            else
                return otherFileInfo.Value.Length.Value;
        }

        public static async Task InvalidateFileDataInPersistentCache(WatcherContext context)
        {
            if (
                Global.PersistentCacheDestAndHistoryFolders
                && context.IsSrcPath
                && (!Global.BidirectionalMirror || context.ForHistory)
            )
            {
                var otherFullName = await GetOtherFullName(context);
                var longOtherFullName = Extensions.GetLongPath(otherFullName);

                var otherDirName = Extensions.GetDirPathWithTrailingSlash(FolderSyncNetSource.Path.GetDirectoryName(otherFullName));
                var longOtherDirName = Extensions.GetLongPath(otherDirName);

                using (await Global.PersistentCacheLocks.LockAsync(longOtherDirName.ToUpperInvariantOnWindows(), context.Token))
                {
                    var cachedFileInfos = await ReadFileInfoCache(longOtherDirName, context.ForHistory);

                    if (cachedFileInfos != null)    //TODO: if cachedFileInfos == null then scan entire folder and create cached file infos file
                    {
                        cachedFileInfos.Remove(GetNonFullName(longOtherFullName).ToUpperInvariantOnWindows());

                        await SaveFileInfoCache(cachedFileInfos, longOtherDirName, context.ForHistory);
                    }
                }
            }
        }   //public static async Task InvalidateFileDataInPersistentCache(Context context)
    }
}