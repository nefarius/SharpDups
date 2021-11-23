using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using Xnlab.SharpDups.Logic;
using Xnlab.SharpDups.Model;

namespace Xnlab.SharpDups.Runner
{
    public enum RunnerMode
    {
        Find,
        Compare,
        Test
    }

    public class Options
    {
        [Option('d', "directory", Required = true, HelpText = "The directory to scan.")]
        public string Path { get; set; }

        [Option('m', "mode", HelpText = "The mode to use (Find, Compare, Test).", Default = RunnerMode.Test)]
        public RunnerMode Mode { get; set; }

        [Option('e', "export", HelpText = "Export found duplicates paths to this text file.")]
        public string ExportFile { get; set; }

        [Option('r', "remove", HelpText = "If set, duplicates will be deleted from disk.")]
        public bool Remove { get; set; }
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            AppDomain.MonitoringIsEnabled = true;

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    if (!Directory.Exists(o.Path))
                    {
                        Console.WriteLine("Please make sure folder {0} exist", o.Path);
                        return;
                    }

                    var folder = o.Path;
                    var workers = 10;
                    var startTime = AppDomain.CurrentDomain.MonitoringTotalProcessorTime;
                    var allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;

                    Console.WriteLine("Invoked in {0} mode", o.Mode);
                    Console.WriteLine("Running...");

                    switch (o.Mode)
                    {
                        case RunnerMode.Find:
                            var detector = new ProgressiveDupDetector();
                            Run(detector, workers, folder);
                            break;
                        case RunnerMode.Compare:
                            RunAll(workers, folder);
                            break;
                        case RunnerMode.Test:
                            PerfAll(workers, folder);
                            break;
                    }

                    Log(
                        $"Took: {(AppDomain.CurrentDomain.MonitoringTotalProcessorTime - startTime).TotalMilliseconds:#,###} ms",
                        true);
                    Log(
                        $"Allocated: {(AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocated) / 1024:#,#} kb",
                        true);
                    Log($"Peak Working Set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024:#,#} kb", true);

                    for (var index = 0; index <= GC.MaxGeneration; index++)
                        Log($"Gen {index} collections: {GC.CollectionCount(index)}", true);
                    Log(string.Empty, true);
                });
        }

        private static void PerfAll(int workers, string folder)
        {
            foreach (var detector in new[]
                { (IDupDetector)new ProgressiveDupDetector(), new DupDetectorV2(), new DupDetector() })
                Perf(detector, workers, folder, 2);
        }

        private static void Perf(IDupDetector dupDetector, int workers, string folder, int times)
        {
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            var timer = new Stopwatch();
            DupResult result = default;

            if (times <= 0)
                times = 10;

            timer.Start();

            for (var i = 0; i < times; i++) result = dupDetector.Find(files, workers);

            timer.Stop();

            Log(
                string.Format("Dup method: {0}, workers: {1}, groups: {2}, times: {3}, avg elapse: {4}", dupDetector,
                    workers, result.Duplicates.Count, times,
                    TimeSpan.FromMilliseconds(timer.ElapsedMilliseconds / times)), true);
        }

        private static void RunAll(int workers, string folder)
        {
            foreach (var detector in new[]
                { (IDupDetector)new ProgressiveDupDetector(), new DupDetectorV2(), new DupDetector() })
                Run(detector, workers, folder);
        }

        private static void Run(IDupDetector dupDetector, int workers, string folder)
        {
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            var result = dupDetector.Find(files, workers);
            foreach (var dup in result.Duplicates.Where(x => x != null))
            {
                var dupItems = dup.Items.OrderByDescending(f => f.ModifiedTime);
                var latestItem = dupItems.First();
                Log("\tLatest one:");
                Log($"\t\t{latestItem.FileName}");
                var remainingItems = dupItems.Skip(1).ToArray();
                Log($"\tDup items:{remainingItems.Length}");
                foreach (var item in remainingItems)
                {
                    Log($"\t\t{item.FileName}");
                }
                Log(string.Empty);
            }

            Log(string.Empty);
            Log($"Failed to process: {result.FailedToProcessFiles.Count}", true);
            foreach (var item in result.FailedToProcessFiles) Log($"\t{item}");

            Log($"Dup groups: {result.Duplicates.Count}", true);
            Log($"Total files: {result.TotalFiles}", true);
            Log($"Total compared files: {result.TotalComparedFiles}", true);
            Log($"Total file bytes: {result.TotalBytesInComparedFiles}", true);
            Log($"Total read bytes: {result.TotalReadBytes}", true);
        }

        private static void Log(string text, bool logToFile = false)
        {
            Console.WriteLine(text);
            if (logToFile)
                File.AppendAllText("log.txt", text + "\r\n");
        }
    }
}