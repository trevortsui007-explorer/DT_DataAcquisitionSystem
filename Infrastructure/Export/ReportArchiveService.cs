using System.Collections.Generic;
using System.IO;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using ICSharpCode.SharpZipLib.Zip;

namespace DT_DataAcquisitionSystem.Infrastructure.Export
{
    public class ReportArchiveService : IReportArchiveService
    {
        public void CreateZip(string zipPath, IEnumerable<string> filePaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            using (var output = File.Create(zipPath))
            using (var zip = new ZipOutputStream(output))
            {
                zip.SetLevel(3);
                foreach (string filePath in filePaths)
                {
                    var entry = new ZipEntry(Path.GetFileName(filePath))
                    {
                        DateTime = File.GetLastWriteTime(filePath),
                        Size = new FileInfo(filePath).Length
                    };
                    zip.PutNextEntry(entry);
                    using (var input = File.OpenRead(filePath))
                    {
                        input.CopyTo(zip);
                    }
                    zip.CloseEntry();
                }
            }
        }
    }
}
