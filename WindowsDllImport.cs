//
// Copyright (c) Roland Pihlakas 2019 - 2022
// roland@simplify.ee
//
// Roland Pihlakas licenses this file to you under the GNU Lesser General Public License, ver 2.1.
// See the LICENSE file for more information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FolderSync
{
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


        //http://social.msdn.microsoft.com/Forums/en/csharpgeneral/thread/44bf304f-0e6b-4079-89c7-ee02763832fa
        public const int PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
        public const int PROCESS_MODE_BACKGROUND_END = 0x00200000;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr handle, int priorityClass);

    }   //internal static class WindowsDllImport 
}
