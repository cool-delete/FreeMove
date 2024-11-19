using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FreeMove.IO;

internal class MoveOperationFile : IOOperation
{
    // Takend form the summary of CopyToAsync
    const int DefaultBufferSize = 81920;
    CancellationTokenSource cts = new CancellationTokenSource();

    bool sameDrive;
    string pathFrom;
    string pathTo;

    public MoveOperationFile(string pathFrom, string pathTo)
    {
        sameDrive = string.Equals(Path.GetPathRoot(pathFrom), Path.GetPathRoot(pathTo), StringComparison.OrdinalIgnoreCase);
        this.pathFrom = pathFrom;
        this.pathTo = pathTo;
        if (Form1.Singleton.chkBox_createDest.Checked && !Directory.Exists(pathTo))
        {
            try
            {
                Directory.CreateDirectory(Directory.GetParent(pathTo).FullName);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (e is UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException("Lacking required permissions to create the destination directory. Try running as administrator.");
                }
                else
                {
                    throw new IOException("Unable to create the destination directory.");
                }
            }
        }
    }

    public override void Cancel() => cts.Cancel();


    public override async Task Run()
    {
        OnStart(EventArgs.Empty);
        try
        {
            if (sameDrive)
            {
                try
                {
                    await Task.Run(() => File.Move(pathFrom, pathTo), cts.Token);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    throw new MoveFailedException("Exception encountered while moving on the same drive", e);
                }
            }
            else
            {
                byte[] writeBuffer = new byte[DefaultBufferSize];
                byte[] readBuffer = new byte[DefaultBufferSize];
                byte[] temp = new byte[DefaultBufferSize]; ;

                using (FileStream source = new FileStream(pathFrom, FileMode.Open, FileAccess.Read))
                {
                    using FileStream dest = new FileStream(pathTo, FileMode.CreateNew, FileAccess.Write);

                    Task<int> read;
                    Task write = null;

                    long fileLength = source.Length;
                    long totalBytes = 0;
                    int currentBlockSize = 0;

                    try
                    {
                        currentBlockSize = await source.ReadAsync(writeBuffer, 0, DefaultBufferSize, cts.Token);
                        while (currentBlockSize > 0)
                        {
                            read = source.ReadAsync(readBuffer, 0, DefaultBufferSize, cts.Token);
                            totalBytes += currentBlockSize;

                            if (write != null)
                            {
                                await write;
                                Swap(ref writeBuffer, ref temp);
                            }

                            write = dest.WriteAsync(writeBuffer, 0, currentBlockSize, cts.Token);
                            currentBlockSize = await read;
                            Swap(ref temp, ref readBuffer);
                        }

                        if (write != null)
                        {
                            await write;
                        }
                    }
                    catch (Exception e)
                    {
                        throw new CopyFailedException("Exception encountered while copying directory", e);
                    }
                }
                
                cts.Token.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(pathFrom);
                }
                catch (Exception e)
                {
                    throw new DeleteFailedException("Exception encountered while removing duplicate file in the old location", e);
                }
            }
        }
        finally
        {
            OnEnd(EventArgs.Empty);
        }
    }

    private void Swap(ref byte[] a, ref byte[] b)
    {
        byte[] c = a;
        a = b;
        b = c;
    }
}
