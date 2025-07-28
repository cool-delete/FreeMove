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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using static System.Windows.Forms.LinkLabel;
using System.Security.Principal;

namespace FreeMove
{
    public partial class Form1 : Form
    {
        public static Form1 Singleton;

        bool safeMode = true;

        #region Initialization
        public Form1()
        {
            Singleton = this;
            //Initialize UI elements
            InitializeComponent();
            this.AllowDrop = true;
            // 启用拖放功能
            this.textBox_From.AllowDrop = true;
            this.textBox_To.AllowDrop = true;
            // 挂载拖放事件
            this.textBox_From.DragEnter += new DragEventHandler(TextBox_DragEnter);
            this.textBox_To.DragEnter += new DragEventHandler(TextBox_DragEnter);
            this.textBox_From.DragDrop += new DragEventHandler(TextBox_DragDrop);
            this.textBox_To.DragDrop += new DragEventHandler(TextBox_DragDrop);
            this.DragEnter += new DragEventHandler(TextBox_DragEnter); // 为主窗体挂载 DragEnter 事件
            this.DragDrop += new DragEventHandler(TextBox_DragDrop);   // 为主窗体挂载 DragDrop 事件       
        }

        public Form1(string[] args) : this()
        {
            textBox_From.Text = string.Join(" ", args);
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            SetToolTips();

            //Check whether the program is set to update on its start
            if (Settings.AutoUpdate)
            {
                //Update the menu item accordingly
                checkOnProgramStartToolStripMenuItem.Checked = true;
                //Start a background update task
                Updater updater = await Task<bool>.Run(() => Updater.SilentCheck());
                //If there is an update show the update dialog
                if (updater != null) updater.ShowDialog();
            }
            switch(Settings.PermCheck)
            {
                case Settings.PermissionCheckLevel.None:
                    noneToolStripMenuItem.Checked = true;
                    fastToolStripMenuItem.Checked = false;
                    fullToolStripMenuItem.Checked = false;
                    break;
                case Settings.PermissionCheckLevel.Fast:
                    noneToolStripMenuItem.Checked = false;
                    fastToolStripMenuItem.Checked = true;
                    fullToolStripMenuItem.Checked = false;
                    break;
                case Settings.PermissionCheckLevel.Full:
                    noneToolStripMenuItem.Checked = false;
                    fastToolStripMenuItem.Checked = false;
                    fullToolStripMenuItem.Checked = true;
                    break;
            }
        }

        #endregion

        private bool PreliminaryCheck(string source, string destination, out bool isFile)
        {
            //Check for errors before copying
            try
            {
                IOHelper.CheckDirectories(source, destination, safeMode, out isFile);
            }
            catch(AggregateException ae)
            {
                var msg = "";
                foreach (var ex in ae.InnerExceptions)
                {
                    msg += ex.Message + "\n";
                }
                MessageBox.Show(msg, "Error");
                isFile = false;
                return false;
            }
            return true;
        }

        private async void Begin()
        {
            Enabled = false;

            string source = textBox_From.Text.Replace("\"", string.Empty);
            string destination = textBox_To.Text.Replace("\"", string.Empty);

            if (destination.EndsWith($"{Path.DirectorySeparatorChar}")
                || destination.EndsWith($"{Path.AltDirectorySeparatorChar}"))
            {
                destination = Path.Combine(
                    destination.Length > 3
                    ? destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : destination, Path.GetFileName(source));
            }
            else
            {
                if (Path.HasExtension(source))
                    destination = Path.ChangeExtension(destination, Path.GetExtension(source));
            }

            string currentDir = null;
            bool currentDirMov = false;

            if (PreliminaryCheck(source, destination, out bool isFile))
            {
                try
                {
                    source = IOHelper.NormalizePath(source);
                    destination = IOHelper.NormalizePath(destination);
                    currentDir = IOHelper.NormalizePath(Directory.GetCurrentDirectory());

                    if (source.ToLower().Contains(currentDir.ToLower()))
                    {
                        Directory.SetCurrentDirectory(Path.GetPathRoot(currentDir));
                        currentDirMov = true;
                    }

                    if (isFile)
                    {
                        await BeginMoveFile(source, destination);
                        IOHelper.MakeFileLink(destination, source);
                    }
                    else
                    {
                        await BeginMoveDirectory(source, destination);
                        IOHelper.MakeDirLink(destination, source);
                    }

                    if (chkBox_originalHidden.Checked)
                    {
                        DirectoryInfo olddir = new DirectoryInfo(source);
                        var attrib = File.GetAttributes(source);
                        olddir.Attributes = attrib | FileAttributes.Hidden;
                    }

                    MessageBox.Show(this, "Done!");
                }
                catch (IO.CopyFailedException ex)
                {
                    switch (MessageBox.Show(this, string.Format($"Do you want to undo the changes?\n\nDetails:\n{ex.InnerException.Message}"), ex.Message, MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1))
                    {
                        case DialogResult.Yes:
                            try
                            {
                                if (isFile)
                                {
                                    File.Delete(destination);
                                }
                                else
                                {
                                    Directory.Delete(destination, true);
                                }
                            }
                            catch (Exception ie)
                            {
                                MessageBox.Show(this, ie.Message, "Could not remove copied contents. Try removing manually");
                            }
                            break;
                        case DialogResult.No:
                            // MessageBox.Show(this, ie.Message, "Could not remove copied contents. Try removing manually");
                            break;
                    }
                }
                catch (IO.DeleteFailedException ex)
                {
                    switch (MessageBox.Show(this, string.Format($"Do you want to undo the changes?\n\nDetails:\n{ex.InnerException.Message}"), ex.Message, MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1))
                    {
                        case DialogResult.Yes:
                            try
                            {
                                if (isFile)
                                    await BeginMoveFile(destination, source);
                                else
                                    await BeginMoveDirectory(destination, source);
                            }
                            catch (Exception ie)
                            {
                                MessageBox.Show(this, ie.Message, "Could not move back contents. Try moving manually");
                            }
                            break;
                        case DialogResult.No:
                            // MessageBox.Show(this, ie.Message, "Could not remove copied contents. Try removing manually");
                            break;
                    }
                }
                catch (IO.MoveFailedException ex)
                {
                    MessageBox.Show(this, string.Format($"Details:\n{ex.InnerException.Message}"), ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (OperationCanceledException)
                {
                    switch (MessageBox.Show(this, string.Format($"Do you want to undo the changes?"), "Cancelled", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1))
                    {
                        case DialogResult.Yes:
                            try
                            {
                                if (isFile)
                                {
                                    if (File.Exists(destination))
                                        File.Delete(destination);
                                }
                                else
                                {
                                    if (Directory.Exists(destination))
                                        Directory.Delete(destination, true);
                                }
                                
                            }
                            catch (Exception ie)
                            {
                                MessageBox.Show(this, ie.Message, "Could not remove copied contents. Try removing manually");
                            }
                            break;
                    }
                }
                finally
                {
                    if (currentDir != null && currentDirMov)
                    {
                        currentDir = IOHelper.GetDeepestExistingDirectory(currentDir);
                        Directory.SetCurrentDirectory(currentDir);
                    }
                }
            }
            Enabled = true;
        }

        private async Task BeginMoveFile(string source, string destination)
        {
            using (ProgressDialog progressDialog = new ProgressDialog("Moving the file..."))
            {
                IO.MoveOperationFile moveOp = IOHelper.MoveFile(source, destination);

                moveOp.ProgressChanged += (sender, e) => progressDialog.UpdateProgress(e);
                moveOp.End += (sender, e) => progressDialog.Invoke((Action)progressDialog.Close);

                progressDialog.CancelRequested += (sender, e) =>
                {
                    if (DialogResult.Yes == MessageBox.Show(this, "Are you sure you want to cancel?", "Cancel confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2))
                    {
                        moveOp.Cancel();
                        progressDialog.BeginInvoke(new Action(() => progressDialog.Cancellable = false));
                    }
                };

                Task task = moveOp.Run();

                progressDialog.ShowDialog(this);
                try
                {
                    await task;
                }
                finally
                {
                    progressDialog.Close();
                }
            }
        }

        private async Task BeginMoveDirectory(string source, string destination)
        {
            //Move files
            using (ProgressDialog progressDialog = new ProgressDialog("Moving files..."))
            {
                IO.MoveOperationDir moveOp = IOHelper.MoveDir(source, destination);

                moveOp.ProgressChanged += (sender, e) => progressDialog.UpdateProgress(e);
                moveOp.End += (sender, e) => progressDialog.Invoke((Action)progressDialog.Close);

                progressDialog.CancelRequested += (sender, e) =>
                {
                    if (DialogResult.Yes == MessageBox.Show(this, "Are you sure you want to cancel?", "Cancel confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2))
                    {
                        moveOp.Cancel();
                        progressDialog.BeginInvoke(new Action(() =>  progressDialog.Cancellable = false));
                    }
                };

                Task task = moveOp.Run();

                progressDialog.ShowDialog(this);
                try
                {
                    await task;
                }
                finally
                {
                    progressDialog.Close();
                }
            }
        }

        //Configure tooltips
        private void SetToolTips()
        {
            ToolTip Tip = new ToolTip()
            {
                ShowAlways = true,
                AutoPopDelay = 5000,
                InitialDelay = 600,
                ReshowDelay = 500
            };
            Tip.SetToolTip(this.textBox_From, "Select the folder you want to move");
            Tip.SetToolTip(this.textBox_To, "Select where you want to move the folder");
            Tip.SetToolTip(this.chkBox_originalHidden, "Select whether you want to hide the shortcut which is created in the old location or not");
        }

        private void Reset()
        {
            textBox_From.Text = "";
            textBox_To.Text = "";
            textBox_From.Focus();   
        }

        public static void Unauthorized(Exception ex)
        {
            MessageBox.Show(Properties.Resources.ErrorUnauthorizedMoveDetails + ex.Message, "Error details");
        }
        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        #region Event Handlers
        
        private void Button_Move_Click(object sender, EventArgs e)
        {
            // 检查当前是否已经是管理员权限
            if (IsAdministrator())
            {
                // 如果已经是，直接执行移动操作
                Begin();
            }
            else
            {
                // 如果不是管理员，则请求提权并重新启动程序
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                var startInfo = new ProcessStartInfo(exeName)
                {
                    // 使用 "runas" 动词，这会触发 UAC 弹窗
                    Verb = "runas",
                    // 把当前输入框的内容作为参数传递给新进程
                    Arguments = $"\"{textBox_From.Text}\" \"{textBox_To.Text}\""
                };

                try
                {
                    Process.Start(startInfo);
                    // 关闭当前的普通权限进程
                    Application.Exit();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 如果用户在UAC弹窗中点击了“否”，会触发一个异常
                    // 我们可以在这里给用户一个提示，或者什么都不做
                    MessageBox.Show("操作已被取消，因为需要管理员权限才能移动。");
                }
            }
        }
        //Show a directory picker for the source directory
        private void Button_BrowseFrom_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                textBox_From.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        //Show a directory picker for the destination directory
        private void Button_BrowseTo_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                textBox_To.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        //Start on enter key press
        private void TextBox_To_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Begin();
            }
        }

        //Close the form
        private void Button_Close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void OpenURL(string url)
        {
            var proc = new ProcessStartInfo(url)
            {
                UseShellExecute = true
            };
            Process.Start(proc);
        }

        //Open GitHub page
        private void GitHubToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenURL("https://github.com/imDema/FreeMove");
        }

        //Open the report an issue page on GitHub
        private void ReportAnIssueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenURL("https://github.com/imDema/FreeMove/issues/new");
        }

        //Show an update dialog
        private void CheckNowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new Updater(false).ShowDialog();
        }

        //Set to check updates on program start
        private void CheckOnProgramStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            checkOnProgramStartToolStripMenuItem.Checked = !checkOnProgramStartToolStripMenuItem.Checked;
            Settings.AutoUpdate = checkOnProgramStartToolStripMenuItem.Checked;
        }
        #endregion

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string msg = String.Format(Properties.Resources.AboutContent, System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion);
            MessageBox.Show(msg, "About FreeMove");
        }

        private void SafeModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Properties.Resources.DisableSafeModeMessage, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                safeMode = false;
                safeModeToolStripMenuItem.Checked = false;
                safeModeToolStripMenuItem.Enabled = false;
            }
        }

        private void NoneToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.PermCheck = Settings.PermissionCheckLevel.None;
            noneToolStripMenuItem.Checked = true;
            fastToolStripMenuItem.Checked = false;
            fullToolStripMenuItem.Checked = false;
        }

        private void FastToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.PermCheck = Settings.PermissionCheckLevel.Fast;
            noneToolStripMenuItem.Checked = false;
            fastToolStripMenuItem.Checked = true;
            fullToolStripMenuItem.Checked = false;
        }

        private void FullToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.PermCheck = Settings.PermissionCheckLevel.Full;
            noneToolStripMenuItem.Checked = false;
            fastToolStripMenuItem.Checked = false;
            fullToolStripMenuItem.Checked = true;
        }
        
        /// <summary>
        /// 处理文件被拖放到输入框中的事件
        /// </summary>
    /// <summary>
    /// 处理文件被拖入输入框上方的事件
    /// </summary>
        private void TextBox_DragEnter(object sender, DragEventArgs e)
        {
            // 检查拖入的是否是文件
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
            // 如果是文件，则显示“复制”的鼠标效果
            e.Effect = DragDropEffects.Copy;
            }
            else
            {
            // 如果不是文件，则显示“禁止”的鼠标效果
            e.Effect = DragDropEffects.None;
            }
        }

    /// <summary>
    /// 处理文件被拖放到输入框中的事件
    /// </summary>
        private void TextBox_DragDrop(object sender, DragEventArgs e)
        {
            // 将拖放的数据转换为字符串数组（因为用户可能拖入多个文件）
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            // 确保确实有文件被拖入
            if (files != null && files.Length > 0)
            {
                // 将 sender（事件触发者）转换为 TextBox 控件
                TextBox textBox = sender as TextBox;
                if (textBox != null)
                {
                    // 我们只取第一个文件的路径，并设置到文本框中
                    textBox.Text = files[0];
                }
            }
        }
    }
}
