// FreeMove -- Move directories without breaking shortcuts or installations 
//    Copyright(C) 2020  Luca De Martini

//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.

//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//    GNU General Public License for more details.

//    You should have received a copy of the GNU General Public License
//    along with this program.If not, see<http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FreeMove
{
    static class IOHelper
    {
        static string[] Blacklist = 
        { 
            @"C:\Windows", 
            @"C:\Windows\System32", 
            @"C:\Windows\Config", 
            @"C:\ProgramData" 
        };

        #region SymLink
        //External dll functions
        [DllImport("kernel32.dll")]
        static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        enum SymbolicLink
        {
            File = 0,
            Directory = 1
        }

        public static bool MakeDirLink(string directory, string symlink)
        {
            return CreateSymbolicLink(symlink, directory, SymbolicLink.Directory);
        }

        public static bool MakeFileLink(string directory, string symlink)
        {
            // 启动一个隐藏的命令行进程来执行 mklink /J 命令
            var process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            // /c 表示执行完命令后就关闭窗口
            // 我们用引号把路径包起来，防止路径中的空格导致问题
            process.StartInfo.Arguments = $"/c mklink /J \"{symlink}\" \"{directory}\"";
            process.StartInfo.CreateNoWindow = true; // 不创建窗口
            process.StartInfo.UseShellExecute = false; // 不使用操作系统外壳
    
            process.Start();
            process.WaitForExit(); // 等待命令执行完成
    
            // 如果命令成功执行，其退出代码为 0
            return process.ExitCode == 0;
        }
        #endregion

        public static IO.MoveOperationFile MoveFile(string source, string destination)
        {
            return new IO.MoveOperationFile(source, destination);
        }

        public static IO.MoveOperationDir MoveDir(string source, string destination)
        {
            return new IO.MoveOperationDir(source, destination);
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(new Uri(path).LocalPath)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string GetDeepestExistingDirectory(string fullPath)
        {
            string currentPath = fullPath;
            while (true)
            {
                if (Directory.Exists(currentPath))
                {
                    return currentPath;
                }

                string parentDir = Path.GetDirectoryName(currentPath);
                if (parentDir == null || parentDir == currentPath)
                {
                    return null;
                }

                currentPath = parentDir;
            }
        }

        public static void CheckDirectories(string source, string destination, bool safeMode, out bool isFile)
        {
            isFile = false;
            List<Exception> exceptions = new List<Exception>();
            //Check for correct file path format
            try
            {
                Path.GetFullPath(source);
                Path.GetFullPath(destination);
            }
            catch (Exception e)
            {
                exceptions.Add(new Exception("Invalid path", e));
            }
            string pattern = @"^[A-Za-z]:\\{1,2}";
            if (!Regex.IsMatch(source, pattern) || !Regex.IsMatch(destination, pattern))
            {
                exceptions.Add(new Exception("Invalid path format"));
            }

            //Check if the chosen directory is blacklisted
            foreach (string item in Blacklist)
            {
                if (item.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    exceptions.Add(new Exception($"The \"{source}\" directory cannot be moved."));
                }
            }

            //Check if folder is critical
            if (safeMode && (
                 source.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
                 || source.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)))
            {
                exceptions.Add(new Exception($"It's recommended not to move the {source} directory, you can disable safe mode in the Settings tab to override this check"));
            }

            //Check for existence of the source
            try
            {
                isFile = !File.GetAttributes(source).HasFlag(FileAttributes.Directory);
            }
            catch (FileNotFoundException e)
            {
                exceptions.Add(new Exception("Source does not exist", e));
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            // Check the destination
            if (destination.EndsWith($"{Path.DirectorySeparatorChar}") 
                || destination.EndsWith($"{Path.AltDirectorySeparatorChar}"))
            {
                if (Directory.Exists(destination))
                    exceptions.Add(new Exception("Destination already contains a folder with the same name"));
                else if (File.Exists(destination))
                    exceptions.Add(new Exception("Destination already contains a file with the same name"));
            }
            else
            {
                if (Directory.Exists(destination))
                    exceptions.Add(new Exception("A folder with the same name as the destination already exists"));
                else if (File.Exists(destination))
                    exceptions.Add(new Exception("A file with the same name as the destination already exists"));
            }

            try
            {
                if (!Form1.Singleton.chkBox_createDest.Checked && !Directory.Exists(Directory.GetParent(destination).FullName))
                    exceptions.Add(new Exception("Destination folder does not exist"));
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            // Next checks rely on the previous so if there was any exception return
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            //Check admin privileges
            string TestFile = Path.Combine(Path.GetDirectoryName(source), "deleteme");
            int ti;
            for (ti = 0; File.Exists(TestFile + ti.ToString()) ; ti++); // Change name if a file with the same name already exists
            TestFile += ti.ToString();

            try
            {
                // DEPRECATED // System.Security.AccessControl.DirectorySecurity ds = Directory.GetAccessControl(source);
                //Try creating a file to check permissions
                File.Create(TestFile).Close();
            }
            catch (UnauthorizedAccessException e)
            {
                exceptions.Add(new Exception("You do not have the required privileges to move the directory.\nTry running as administrator", e));
            }
            finally
            {
                if (File.Exists(TestFile))
                    File.Delete(TestFile);
            }

            //Try creating a symbolic link to check permissions
            try
            {
                if (!CreateSymbolicLink(TestFile, Path.GetDirectoryName(destination), SymbolicLink.Directory))
                    exceptions.Add(new Exception("Could not create a symbolic link.\nTry running as administrator"));
            }
            finally
            {
                if (Directory.Exists(TestFile))
                    Directory.Delete(TestFile);
            }

            // Next checks rely on the previous so if there was any exception return
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            DriveInfo dstDrive = new(Path.GetPathRoot(destination));
            long size = 0;

            if (isFile)
            {
                size = new FileInfo(source).Length;
            }
            else
            {
                DirectoryInfo dirInf = new DirectoryInfo(source);
                foreach (FileInfo file in dirInf.GetFiles("*", SearchOption.AllDirectories))
                {
                    size += file.Length;
                }
            }

            try
            {
                if (dstDrive.AvailableFreeSpace < size)
                    exceptions.Add(new Exception($"There is not enough free space on the {dstDrive.Name} disk. {size / 1000000}MB required, {dstDrive.AvailableFreeSpace / 1000000} available."));
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            //If set to do full check try to open for write all files
            if (Settings.PermCheck != Settings.PermissionCheckLevel.None)
            {
                var exceptionBag = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                Action<string> CheckFile = (file) =>
                {
                    FileInfo fi = new FileInfo(file);
                    FileStream fs = null;
                    try
                    {
                        fs = fi.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (Exception ex)
                    {
                        exceptionBag.Add(ex);
                    }
                    finally
                    {
                        if (fs != null)
                            fs.Dispose();
                    }
                };
                if (isFile)
                {
                    CheckFile(source);
                }
                else if (Settings.PermCheck == Settings.PermissionCheckLevel.Fast)
                {
                    Parallel.ForEach(Directory.GetFiles(source, "*.exe", SearchOption.AllDirectories), CheckFile);
                    Parallel.ForEach(Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories), CheckFile);
                } 
                else
                {
                    Parallel.ForEach(Directory.GetFiles(source, "*", SearchOption.AllDirectories), CheckFile);
                }

                exceptions.AddRange(exceptionBag);
            }
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);
        }
    }
}
