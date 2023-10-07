//
// Copyright (c) Roland Pihlakas 2019 - 2023
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nito.AsyncEx;
using NReco.Text;

namespace FolderSync
{
#pragma warning disable S2223   //Warning	S2223	Change the visibility of 'xxx' or make it 'const' or 'readonly'.
    internal static class Global
    {
        public static IConfigurationRoot Configuration;
        public static readonly CancellationTokenSource CancellationToken = new CancellationTokenSource();



        public static bool UseIdlePriority = false;
        public static bool UseBackgroundMode = false;
        public static List<long> Affinity = new List<long>();


        public static int DirlistReadDelayMs = 0;
        public static int FileWriteDelayMs = 0;
        public static int ReadBufferKB = 1024;
        public static int WriteBufferKB = 1024;
        public static int BufferReadDelayMs = 0;
        public static int BufferWriteDelayMs = 0;


        public static bool ShowErrorAlerts = true;
        public static bool LogInitialScan = false;
        public static bool LogToFile = false;
        public static bool AddTimestampToNormalLogEntries = true;


        public static bool UsePolling = false;
        public static int PollingDelay = 60;


        public static int RetryCountOnEmptyDirlist = 0;
        public static int RetryCountOnSrcFileOpenError = 5;
        public static int FSOperationTimeout = 3600; //60 * 15;
        public static int DirListOperationTimeout = 3600; //60 * 15;
        public static int FileBufferWriteTimeout = 3600; //60 * 15;
        public static int FileBufferReadTimeout = 3600; //60 * 15;


        public static string SrcPath = "";


        public static bool AllowVSS = false; //true;     //TODO!!!


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


        public static HashSet<string> MirrorWatchedExtension = new HashSet<string>() { "*" };
        public static HashSet<string> MirrorWatchedFileNames = new HashSet<string>() { };
        public static HashSet<string> MirrorExcludedExtensions = new HashSet<string>() { "*~", "tmp" };

        public static List<string> MirrorIgnorePathsStartingWithList = new List<string>();
        public static List<string> MirrorIgnorePathsContainingList = new List<string>();
        public static List<string> MirrorIgnorePathsEndingWithList = new List<string>();

        public static bool MirrorIgnorePathsContainingACHasAny = false;
        public static AhoCorasickDoubleArrayTrie<bool> MirrorIgnorePathsContainingAC = new AhoCorasickDoubleArrayTrie<bool>();

        public static string MirrorDestPath = "";


        public static bool EnableHistory = false;
        public static HashSet<string> HistoryWatchedExtension = new HashSet<string>() { "*" };
        public static HashSet<string> HistoryWatchedFileNames = new HashSet<string>() { };
        public static HashSet<string> HistoryExcludedExtensions = new HashSet<string>() { "*~", "bak", "tmp" };

        public static List<string> HistoryIgnorePathsStartingWithList = new List<string>();
        public static List<string> HistoryIgnorePathsContainingList = new List<string>();
        public static List<string> HistoryIgnorePathsEndingWithList = new List<string>();

        public static bool HistoryIgnorePathsContainingACHasAny = false;
        public static AhoCorasickDoubleArrayTrie<bool> HistoryIgnorePathsContainingAC = new AhoCorasickDoubleArrayTrie<bool>();

        public static string HistoryDestPath = "";

        public static string HistoryVersionFormat = "TIMESTAMP_BEFORE_EXT";
        public static string HistoryVersionSeparator = ".";


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
}
