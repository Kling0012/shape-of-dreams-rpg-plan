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
            if (Stage6Report.TryRun(options)) return 0;
            if (options.Forge)
            {
                var entries = ForgeReport.Measure();
                string forgeReport = ForgeReport.Render(entries);
                if (options.Out != null)
                {
                    string forgePath = Path.GetFullPath(options.Out);
                    string? forgeDirectory = Path.GetDirectoryName(forgePath);
                    if (forgeDirectory != null) Directory.CreateDirectory(forgeDirectory);
                    File.WriteAllText(forgePath, forgeReport, new UTF8Encoding(false));
                }
                if (options.MetricsJson != null) Metrics.WriteForge(options.MetricsJson, entries);
                Console.Write(forgeReport);
                return 0;
            }
            if (options.StarEfficiency)
            {
                var measurement = StarEfficiencyReport.Measure();
                string efficiencyReport = StarEfficiencyReport.Render(measurement);
                if (options.Out != null)
                {
                    string efficiencyPath = Path.GetFullPath(options.Out);
                    string? efficiencyDirectory = Path.GetDirectoryName(efficiencyPath);
                    if (efficiencyDirectory != null) Directory.CreateDirectory(efficiencyDirectory);
                    File.WriteAllText(efficiencyPath, efficiencyReport, new UTF8Encoding(false));
                }
                if (options.MetricsJson != null) Metrics.WriteStarEfficiency(options.MetricsJson, measurement);
                Console.Write(efficiencyReport);
                return 0;
            }
            if (options.StarValues)
            {
                var measurement = StarValuesReport.Measure();
                string valuesReport = StarValuesReport.Render(measurement);
                if (options.Out != null)
                {
                    string valuesPath = Path.GetFullPath(options.Out);
                    string? valuesDirectory = Path.GetDirectoryName(valuesPath);
                    if (valuesDirectory != null) Directory.CreateDirectory(valuesDirectory);
                    File.WriteAllText(valuesPath, valuesReport, new UTF8Encoding(false));
                }
                if (options.MetricsJson != null) Metrics.WriteStarValues(options.MetricsJson, measurement);
                Console.Write(valuesReport);
                return 0;
            }
            if (options.StarProgressionMode)
            {
                var entries = StarProgressionReport.Measure();
                string progressionReport = StarProgressionReport.Render(entries);
                if (options.Out != null)
                {
                    string path = Path.GetFullPath(options.Out);
                    string? directory = Path.GetDirectoryName(path);
                    if (directory != null) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, progressionReport, new UTF8Encoding(false));
                }
                if (options.MetricsJson != null) Metrics.WriteStarProgression(options.MetricsJson, entries);
                Console.Write(progressionReport);
                return 0;
            }
            if (options.Pressure)
            {
                var entries = PressureReport.Measure();
                string pressureReport = PressureReport.Render(entries);
                if (options.Out != null)
                {
                    string path = Path.GetFullPath(options.Out);
                    string? directory = Path.GetDirectoryName(path);
                    if (directory != null) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, pressureReport, new UTF8Encoding(false));
                }
                if (options.MetricsJson != null) Metrics.WritePressure(options.MetricsJson, entries);
                Console.Write(pressureReport);
                return 0;
            }
            if (options.Sets || options.Equipment)
            {
                string equipmentReport;
                if (options.Sets)
                {
                    var results = SetBalance.MeasureAll();
                    timer.Stop();
                    equipmentReport = SetReport.Render(results, timer.Elapsed);
                    if (options.MetricsJson != null) Metrics.WriteSets(options.MetricsJson, results);
                }
                else
                {
                    var entries = EquipmentReport.Measure();
                    equipmentReport = EquipmentReport.Render(entries);
                    if (options.MetricsJson != null) Metrics.WriteEquipment(options.MetricsJson, entries);
                }
                if (options.Out != null)
                {
                    string path = Path.GetFullPath(options.Out);
                    string? directory = Path.GetDirectoryName(path);
                    if (directory != null) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, equipmentReport, new UTF8Encoding(false));
                }
                Console.Write(equipmentReport);
                return 0;
            }
            if (options.Infinity)
            {
                var infinity = new InfinitySimulation(options);
                infinity.Run();
                if (options.MetricsJson != null) Metrics.WriteInfinity(options.MetricsJson, options, infinity);
                timer.Stop();
                string infinityReport = InfinityReport.Render(options, infinity, timer.Elapsed);
                if (options.Out != null)
                {
                    string infinityPath = Path.GetFullPath(options.Out);
                    string? infinityDirectory = Path.GetDirectoryName(infinityPath);
                    if (infinityDirectory != null) Directory.CreateDirectory(infinityDirectory);
                    File.WriteAllText(infinityPath, infinityReport, new UTF8Encoding(false));
                }
                Console.Write(infinityReport);
                return 0;
            }
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
            if (options.V132Stars)
            {
                var economy = V132Simulation.RunEconomy(options, "secure");
                economy.AddRange(V132Simulation.RunEconomy(options, "greedy"));
                var growth = V132Simulation.RunGrowth(options);
                var sensitivity = V132Simulation.RunGrowthSensitivity(options, V132Report.SensitivityScenarios);
                if (options.MetricsJson != null) Metrics.WriteV132Stars(options.MetricsJson, options, economy, growth, sensitivity);
                timer.Stop();
                string v132Report = V132Report.Render(options, economy, growth, sensitivity, timer.Elapsed);
                if (options.Out != null)
                {
                    string v132Path = Path.GetFullPath(options.Out);
                    string? v132Directory = Path.GetDirectoryName(v132Path);
                    if (v132Directory != null) Directory.CreateDirectory(v132Directory);
                    File.WriteAllText(v132Path, v132Report, new UTF8Encoding(false));
                }
                Console.Write(v132Report);
                return 0;
            }
            var simulation = new Simulation(options);
            simulation.Run();
            if (options.MetricsJson != null) Metrics.WriteExpeditions(options.MetricsJson, options, simulation);
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
