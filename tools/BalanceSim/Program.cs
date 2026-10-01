using System.Diagnostics;
using System.Text;

namespace BalanceSim;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var options = Options.Parse(args);
            if (options.Help)
            {
                Console.WriteLine(Options.Usage);
                return 0;
            }
            var timer = Stopwatch.StartNew();
            var simulation = new Simulation(options);
            simulation.Run();
            timer.Stop();
            string report = Report.Render(options, simulation, timer.Elapsed);
            if (options.Out != null)
            {
                string path = Path.GetFullPath(options.Out);
                string? directory = Path.GetDirectoryName(path);
                if (directory != null) Directory.CreateDirectory(directory);
                File.WriteAllText(path, report, new UTF8Encoding(false));
            }
            Console.Write(report);
            return 0;
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"引数エラー: {e.Message}");
            Console.Error.WriteLine("使い方: --help");
            return 2;
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"出力エラー: {e.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine($"出力エラー: {e.Message}");
            return 1;
        }
    }
}
