using System;
using System.IO;
using System.Threading.Tasks;

namespace helvety.screentools
{
    internal static class NewFileWriter
    {
        /// <summary>
        /// Writes <paramref name="bytes"/> to <c>{baseName}.png</c> in <paramref name="folder"/>, or <c>{baseName}_N.png</c>
        /// when that name is taken. Data goes to a temporary file first and is then moved into place, so a crash or full
        /// disk never leaves a truncated PNG and an existing file is never overwritten.
        /// </summary>
        /// <returns>The path of the written file.</returns>
        internal static async Task<string> WriteNewPngAsync(string folder, string baseName, byte[] bytes)
        {
            // The gallery and folder watcher only look at *.png, so the temp file stays invisible.
            var tempPath = Path.Combine(folder, $"{baseName}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, bytes);
            try
            {
                for (var counter = 0; ; counter++)
                {
                    var candidatePath = Path.Combine(folder, counter == 0 ? $"{baseName}.png" : $"{baseName}_{counter}.png");
                    if (File.Exists(candidatePath))
                    {
                        continue;
                    }

                    try
                    {
                        File.Move(tempPath, candidatePath, overwrite: false);
                        return candidatePath;
                    }
                    catch (IOException) when (File.Exists(candidatePath))
                    {
                        // Lost a race for this name; try the next one.
                    }
                }
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
