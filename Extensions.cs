//
// Copyright (c) Roland Pihlakas 2019 - 2020
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

#define ASYNC
using System;
using System.Collections.Generic;

namespace FolderSync
{
    public static class Extensions
    {
        public static string GetLongPath(string path)
        {
            //@"\\?\" prefix is needed for reading from long paths: https://stackoverflow.com/questions/44888844/directorynotfoundexception-when-using-long-paths-in-net-4-7 and https://superuser.com/questions/1617012/support-of-the-unc-server-share-syntax-in-windows

            if (path.Substring(0, 2) == @"\\")   //network path or path already starting with \\?\
            {
                return path;
            }
            else
            {
                return @"\\?\" + path;
            }
        }
    }
}
