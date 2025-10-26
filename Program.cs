using Dropbox.Api;
using Dropbox.Api.Files;
using System.Formats.Tar;
using System.IO.Compression;

namespace Static_Files_Daily_Backup_Application
{
    internal class Program
    {
        private static string appKey = "5tu5hlabq8ku6bg";
        private static string appSecret = "rl9yda5uh44he0e";
        private static string refreshToken = "dvsLUJDnTtQAAAAAAAAAAV9oOpO_YNsGVh0dxF1B_9B41L6VkpgzHcbX5XMtELXB";
        private static string dropboxBasePath = "/Apps/Static Files Daily Backup";
        static async Task Main(string[] args)
        {
            try
            {
                string jsonFile = File.ReadAllText("D:\\Others\\Static Files Daily Backup Application\\FolderPaths.json");
                List<FolderPath>? folderPath = System.Text.Json.JsonSerializer.Deserialize<List<FolderPath>>(jsonFile);

                foreach (var item in folderPath!)
                {
                    string zipFile = item.sourceFolderPath + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
                    ZipFile.CreateFromDirectory(item.sourceFolderPath, zipFile, CompressionLevel.Optimal, includeBaseDirectory: true);

                    string dateFolderName = $"StaticFilesBackup_{DateTime.Now:MM_dd_yyyy}";
                    string dropboxDateFolderPath = $"{dropboxBasePath}/{dateFolderName}";
                    string dropboxFilePath = $"{dropboxDateFolderPath}/{Path.GetFileName(zipFile)}";

                    Console.WriteLine($"Uploading {zipFile} to Dropbox...");
                    using (var dbx = new DropboxClient(refreshToken, appKey, appSecret, new DropboxClientConfig("StaticFilesBackupApp")))
                    {
                        await UploadFileToDropboxAsync(dbx, zipFile, dropboxFilePath);
                    }
                    Console.WriteLine($"✅ Uploaded {Path.GetFileName(zipFile)} successfully!");
                    File.Delete(zipFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred: {ex.Message}");
            }
        }

        // ✅ Chunked upload implementation
        private static async Task UploadFileToDropboxAsync(DropboxClient dbx, string localFile, string dropboxPath)
        {
            const int chunkSize = 128 * 1024 * 1024; // 128 MB per chunk
            using var fileStream = new FileStream(localFile, FileMode.Open, FileAccess.Read);
            long fileSize = fileStream.Length;

            if (fileSize <= 150 * 1024 * 1024)
            {
                // Simple upload for small files
                await dbx.Files.UploadAsync(
                    dropboxPath,
                    WriteMode.Overwrite.Instance,
                    body: fileStream);
                return;
            }

            // Chunked upload for large files
            byte[] buffer = new byte[chunkSize];
            int bytesRead;
            ulong uploaded = 0;
            UploadSessionStartResult session = null;

            // 1️⃣ Start upload session
            bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
            using (var memStream = new MemoryStream(buffer, 0, bytesRead))
            {
                session = await dbx.Files.UploadSessionStartAsync(body: memStream);
                uploaded += (ulong)bytesRead;
                Console.WriteLine($"Started upload session: {uploaded / (1024 * 1024)} MB uploaded...");
            }

            // 2️⃣ Append chunks
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize)) > 0)
            {
                using var memStream = new MemoryStream(buffer, 0, bytesRead);
                var cursor = new UploadSessionCursor(session.SessionId, uploaded);
                bool isLastChunk = (uploaded + (ulong)bytesRead) >= (ulong)fileSize;

                if (isLastChunk)
                {
                    // 3️⃣ Finish upload session
                    await dbx.Files.UploadSessionFinishAsync(
                        cursor,
                        new CommitInfo(dropboxPath, WriteMode.Overwrite.Instance),
                        body: memStream);
                    Console.WriteLine($"Upload complete: {fileSize / (1024 * 1024)} MB total.");
                }
                else
                {
                    await dbx.Files.UploadSessionAppendV2Async(cursor, body: memStream);
                    uploaded += (ulong)bytesRead;
                    Console.WriteLine($"Uploaded {uploaded / (1024 * 1024)} MB...");
                }
            }
        }
    }
}
