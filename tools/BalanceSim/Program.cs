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
            if (options.Stars)
            {
                var stars = new StarSimulation(options);
                stars.Run();
                timer.Stop();
                string starReport = StarReport.Render(options, stars, timer.Elapsed);
                if (options.Out != null)
                {
                    string starPath = Path.GetFullPath(options.Out);
                    string? starDirectory = Path.GetDirectoryName(starPath);
                    if (starDirectory != null) Directory.CreateDirectory(starDirectory);
                    File.WriteAllText(starPath, starReport, new UTF8Encoding(false));
                }
                Console.Write(starReport);
                return 0;
            }
            if (options.Sets)
            {
                var sets = SetBalance.MeasureAll();
                timer.Stop();
                string setReport = SetReport.Render(sets, timer.Elapsed);
                if (options.Out != null)
                {
                    string setPath = Path.GetFullPath(options.Out);
                    string? setDirectory = Path.GetDirectoryName(setPath);
                    if (setDirectory != null) Directory.CreateDirectory(setDirectory);
                    File.WriteAllText(setPath, setReport, new UTF8Encoding(false));
                }
                Console.Write(setReport);
                return 0;
            }
            var simulation = new Simulation(options);
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
