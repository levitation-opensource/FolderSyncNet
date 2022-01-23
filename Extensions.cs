//
// Copyright (c) Roland Pihlakas 2019 - 2022
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace FolderSync
{
    public static class Extensions
    {
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

        public static string GetLongPath(string path)
        {
            if (!ConfigParser.IsWindows)
                return Path.GetFullPath(path);    //GetFullPath: convert relative path to full path


            //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7 and https://superuser.com/questions/1617012/support-of-the-unc-server-share-syntax-in-windows

            if (path.Substring(0, 2) == @"\\")   //network path or path already starting with \\?\
            {
                return path;
            }
            else
            {
                return @"\\?\" + Path.GetFullPath(path);    //GetFullPath: convert relative path to full path
            }
        }

        public static string GetDirPathWithTrailingSlash(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                return dirPath;

            dirPath = Path.Combine(dirPath, ".");    //NB! add "." in order to ensure that slash is appended to the end of the path
            dirPath = dirPath.Substring(0, dirPath.Length - 1);     //drop the "." again
            return dirPath;
        }

        public static async Task FSOperation(Action func, CancellationToken token)
        {
            //await Task.Run(func).WaitAsync(token);
            func();
        }

        public static async Task<T> FSOperation<T>(Func<T> func, CancellationToken token)
        {
            //var result = await Task.Run(func).WaitAsync(token);
            var result = func();
            return result;
        }

        public static async Task<T[]> DirListOperation<T>(Func<T[]> func, int retryCount, CancellationToken token)
        {
            retryCount = Math.Max(0, retryCount);

            for (int i = -1; i < retryCount; i++)
            { 
                //T result = await Task.Run(func).WaitAsync(token);
                var result = func();

                if (result.Length > 0)
                    return result;

#if DEBUG
                if (result is FileInfo[])
                {
                    bool qqq = true;    //for debugging
                }
#endif

#if !NOASYNC
                await Task.Delay(1000, token);     //TODO: config file?
#else
                token.WaitHandle.WaitOne(1000);
#endif
            }

            return new T[0];
        }

        public static byte[] SerializeBinary<T>(this T obj, bool compress = true)
        {
            var formatter = new BinaryFormatter();
            formatter.Context = new StreamingContext(StreamingContextStates.Persistence);

            using (var mstream = new MemoryStream())
            {
                if (compress)
                {
                    using (var gzStream = new GZipStream(mstream, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        formatter.Serialize(gzStream, obj);
                    }
                }
                else
                {
                    formatter.Serialize(mstream, obj);
                }

                byte[] bytes = new byte[mstream.Length];
                mstream.Position = 0;   //NB! reset stream position
                mstream.Read(bytes, 0, (int)mstream.Length);
                return bytes;
            }
        }

        public static T DeserializeBinary<T>(this byte[] bytes, bool? decompress = null)
        {
            var formatter = new BinaryFormatter();
            formatter.Context = new StreamingContext(StreamingContextStates.Persistence);

            using (var mstream = new MemoryStream(bytes))
            {
                mstream.Position = 0;

                if (decompress == null)     //auto detect
                {
                    if (mstream.ReadByte() == 0x1F && mstream.ReadByte() == 0x8B)
                        decompress = true;

                    mstream.Position = 0;   //reset stream position
                }

                if (decompress == true)
                {
                    using (var gzStream = new GZipStream(mstream, CompressionMode.Decompress, leaveOpen: true))
                    {
                        object result = formatter.Deserialize(gzStream);
                        if (result is T)
                            return (T)result;
                        return default(T);
                    }
                }
                else
                {
                    object result = formatter.Deserialize(mstream);
                    if (result is T)
                        return (T)result;
                    return default(T);
                }
            }
        }
    }
}
