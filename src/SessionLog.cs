using System;
using System.IO;
using System.Text;

internal sealed class SessionLog : IDisposable
{
    private readonly object gate = new object();
    private StreamWriter writer;
    private int characters;
    internal string FilePath { get; private set; }
    internal string Error { get; private set; }
    internal SessionLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
        writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        writer.AutoFlush = true;
    }
    internal void Write(string line)
    {
        lock (gate)
        {
            if (writer == null || characters > 250000) return;
            try
            {
                writer.WriteLine(line);
                characters += line.Length + 2;
                if (characters > 250000) writer.WriteLine("Kayıt sınırına ulaşıldı; bu oturumda yeni satırlar kaydedilmiyor.");
            }
            catch (IOException ex) { Error = ex.Message; }
            catch (UnauthorizedAccessException ex) { Error = ex.Message; }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (writer != null)
            {
                try { writer.Dispose(); } catch (IOException ex) { Error = ex.Message; }
                writer = null;
            }
        }
    }
}
