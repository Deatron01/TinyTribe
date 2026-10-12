using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using TinyTribe.Brains;
using TinyTribe.Sim.Data;

namespace TinyTribe.Host
{
    /// <summary>
    /// The simulation as a console program, for everything that needs many villages and no screen:
    ///
    ///   batch   play and record one village per seed with the scripted bots
    ///   export  turn recorded runs into training data (what each villager saw, could do, chose, earned)
    ///   replay  play recorded runs again and check they come out the same
    ///   serve   step villages for a trainer over stdin/stdout (used by ML/tribe_env.py)
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                Console.WriteLine(Help);
                return 0;
            }
            var options = Options(args);
            try
            {
                var data = GameData.Load(options.TryGetValue("data", out string dir) ? dir : FindData());
                switch (args[0])
                {
                    case "batch": return Batch(data, options);
                    case "export": return Export(data, options);
                    case "replay": return Replay(data, options);
                    case "serve": return Serve(data);
                    case "hashes": return Hashes(data, options);
                    case "advice": return Advice.Run(data, options);
                    default:
                        Console.Error.WriteLine("Unknown command '" + args[0] + "'.\n" + Help);
                        return 2;
                }
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine(problem);
                return 1;
            }
        }

        const string Help =
            "tinytribe-host <command> [options]\n" +
            "  batch   --out RUNS --seeds 1-24 --days 28 [--brains survival|random|idle]\n" +
            "  export  --runs RUNS --out DATASET            (skips runs already exported)\n" +
            "  replay  --runs RUNS                          (checks every run plays again the same)\n" +
            "  serve                                        (binary protocol on stdin/stdout; see ML/tribe_env.py)\n" +
            "  common: --data DIR   the exported workbook (default: Game/TinyTribe/Assets/StreamingAssets/Data)";

        static Dictionary<string, string> Options(string[] args)
        {
            var options = new Dictionary<string, string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                options[args[i].Substring(2)] = hasValue ? args[++i] : "true";
            }
            return options;
        }

        /// <summary>The data folder of the project this program was built in: found by walking up from where it runs.</summary>
        static string FindData()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                {
                    string data = Path.Combine(folder.FullName, "Game", "TinyTribe", "Assets", "StreamingAssets", "Data");
                    if (Directory.Exists(data)) return data;
                }
            throw new DirectoryNotFoundException("Cannot find Game/TinyTribe/Assets/StreamingAssets/Data. Pass --data.");
        }

        static string Need(Dictionary<string, string> options, string name) =>
            options.TryGetValue(name, out string value) ? value : throw new ArgumentException("Missing --" + name);

        static IEnumerable<ulong> Seeds(string text)
        {
            foreach (string part in text.Split(','))
            {
                var ends = part.Split('-');
                ulong from = ulong.Parse(ends[0]), to = ends.Length > 1 ? ulong.Parse(ends[1]) : from;
                for (ulong seed = from; seed <= to; seed++) yield return seed;
            }
        }

        static Dictionary<string, string> HostInfo(string note) => new Dictionary<string, string>
        {
            ["program"] = "tinytribe-host", ["runtime"] = RuntimeInformation.FrameworkDescription, ["os"] = RuntimeInformation.OSDescription,
            ["cores"] = Environment.ProcessorCount.ToString(), ["note"] = note,
        };

        static int Batch(GameData data, Dictionary<string, string> options)
        {
            string runs = Need(options, "out");
            int days = int.Parse(options.TryGetValue("days", out string d) ? d : "28");
            var brains = (BrainKind)Enum.Parse(typeof(BrainKind), options.TryGetValue("brains", out string b) ? b : "survival", true);
            var watch = Stopwatch.StartNew();
            var folders = RunBatch.Run(data, runs, Seeds(Need(options, "seeds")), days, brains, null, HostInfo("batch of " + brains + " villages, " + days + " days each"));
            foreach (string folder in folders)
            {
                var run = RunReplay.Read(folder);
                Console.WriteLine(run.RunId + "  " + run.Result.EndReason + "  alive " + run.Result.Alive + "  achievements " + run.Result.Achievements.Count);
            }
            Console.WriteLine(folders.Count + " runs in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s -> " + Path.GetFullPath(runs));
            return 0;
        }

        static int Export(GameData data, Dictionary<string, string> options)
        {
            string runs = Need(options, "runs"), dataset = Need(options, "out");
            var watch = Stopwatch.StartNew();
            int done = 0, skipped = 0, failed = 0;
            long rows = 0;
            foreach (string folder in Directory.GetDirectories(runs))
            {
                if (!File.Exists(Path.Combine(folder, "run.json"))) continue;
                if (File.Exists(Path.Combine(dataset, Path.GetFileName(folder), "episode.json")))
                {
                    skipped++;
                    continue;
                }
                string exported = DatasetExporter.Export(data, folder, dataset, out string problem);
                if (exported == null)
                {
                    failed++;
                    Console.Error.WriteLine(Path.GetFileName(folder) + ": " + problem);
                    continue;
                }
                var run = RunReplay.Read(folder);
                rows += run.Result != null ? run.Result.Ticks * run.Setup.Agents : 0;
                done++;
                Console.WriteLine("exported " + Path.GetFileName(folder));
            }
            Console.WriteLine(done + " exported (" + rows + " rows), " + skipped + " already there, " + failed + " failed, in "
                              + watch.Elapsed.TotalSeconds.ToString("0.0") + " s -> " + Path.GetFullPath(dataset));
            return failed > 0 ? 1 : 0;
        }

        static int Replay(GameData data, Dictionary<string, string> options)
        {
            int bad = 0, good = 0;
            foreach (string folder in Directory.GetDirectories(Need(options, "runs")))
            {
                if (!File.Exists(Path.Combine(folder, "run.json"))) continue;
                RunReplay.Replay(data, folder, out string problem);
                if (problem == null) good++;
                else
                {
                    bad++;
                    Console.WriteLine(Path.GetFileName(folder) + ": " + problem);
                }
            }
            Console.WriteLine(good + " runs play again the same, " + bad + " do not");
            return bad > 0 ? 1 : 0;
        }

        /// <summary>For finding where two machines or runtimes part ways: the state hash after every tick of a replay.</summary>
        static int Hashes(GameData data, Dictionary<string, string> options)
        {
            long from = long.Parse(options.TryGetValue("from", out string f) ? f : "0"), to = long.Parse(options.TryGetValue("to", out string t) ? t : "1000000");
            var last = RunReplay.Replay(data, Need(options, "run"), out _, to, sim =>
            {
                if (sim.Tick >= from) Console.WriteLine(sim.Tick + " " + sim.ComputeStateHash());
            });
            // With --state FILE, the whole state at the last tick, to compare with another machine's.
            if (options.TryGetValue("state", out string file)) File.WriteAllText(file, StateText.Describe(last));
            return 0;
        }

        // ------------------------------------------------------------------ serve

        /// <summary>
        /// A trainer's villages over stdin/stdout, all numbers little-endian:
        ///   'I' + int32 n + n bytes of JSON (the fields of EnvOptions)  ->  int32 n + JSON (the sizes), then a frame
        ///   'S' + envs x agents x 2 bytes (action, argument)            ->  a frame
        ///   'R'                                                         ->  a frame, every village new
        ///   'Q'                                                         ->  nothing; the program ends
        /// A frame is the pool's arrays one after another: grid, vector (float32), action mask, argument
        /// mask, reward (float32), alive, done, expert; then int32 n + JSON, the villages that ended on that tick.
        /// </summary>
        static int Serve(GameData data)
        {
            using var input = new BufferedStream(Console.OpenStandardInput(), 1 << 16);
            using var output = new BufferedStream(Console.OpenStandardOutput(), 1 << 20);
            EnvPool pool = null;
            byte[] actions = null;
            var four = new byte[4];
            try
            {
                while (true)
                {
                    int op = input.ReadByte();
                    if (op < 0 || op == 'Q') return 0;
                    if (op == 'I')
                    {
                        ReadAll(input, four, 4);
                        var json = new byte[BitConverter.ToInt32(four, 0)];
                        ReadAll(input, json, json.Length);
                        pool?.Dispose();
                        pool = new EnvPool(data, JsonConvert.DeserializeObject<EnvOptions>(Encoding.UTF8.GetString(json)) ?? new EnvOptions());
                        actions = new byte[pool.Envs * pool.Agents * 2];
                        var layout = pool.Layout;
                        var fields = new Dictionary<string, int[]>();
                        foreach (var field in layout.Fields) fields[field.Key] = new[] { field.Value.Start, field.Value.Length };
                        var names = new List<string>();
                        foreach (var action in data.Actions) names.Add(action.Name);
                        var takesArgument = new List<bool>();
                        foreach (var action in data.Actions) takesArgument.Add(pool.SimulationOf(0).TakesArgument(action.Id));
                        WriteJson(output, new Dictionary<string, object>
                        {
                            ["envs"] = pool.Envs, ["agents"] = pool.Agents, ["grid_size"] = layout.GridSize, ["grid_channels"] = layout.GridChannels,
                            ["channel_sizes"] = layout.ChannelSizes, ["grid_length"] = layout.GridLength, ["vector_length"] = layout.VectorLength,
                            ["action_count"] = layout.ActionCount, ["arg_count"] = layout.ArgCount, ["arg_mask_bytes"] = pool.ArgMaskBytes,
                            ["item_scale"] = layout.ItemScale, ["role_scale"] = layout.RoleScale,
                            ["fields"] = fields, ["actions"] = names, ["takes_argument"] = takesArgument,
                            ["workbook"] = data.Workbook, ["workbook_sha256"] = data.WorkbookSha256,
                        });
                        pool.Reset();
                        WriteFrame(output, pool);
                    }
                    else if (op == 'S' && pool != null)
                    {
                        ReadAll(input, actions, actions.Length);
                        pool.Step(actions);
                        WriteFrame(output, pool);
                    }
                    else if (op == 'R' && pool != null)
                    {
                        pool.Reset();
                        WriteFrame(output, pool);
                    }
                    else
                    {
                        Console.Error.WriteLine("serve: unexpected byte " + op + (pool == null ? " before 'I'" : ""));
                        return 2;
                    }
                }
            }
            finally
            {
                pool?.Dispose();
            }
        }

        static void ReadAll(Stream stream, byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int read = stream.Read(buffer, got, count - got);
                if (read <= 0) throw new EndOfStreamException("The trainer went away in the middle of a message.");
                got += read;
            }
        }

        static void WriteJson(Stream stream, object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
            stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
            stream.Write(bytes, 0, bytes.Length);
        }

        static void WriteFrame(Stream stream, EnvPool pool)
        {
            stream.Write(pool.Grid, 0, pool.Grid.Length);
            stream.Write(MemoryMarshal.AsBytes(pool.Vector.AsSpan()));
            stream.Write(pool.ActionMask, 0, pool.ActionMask.Length);
            stream.Write(pool.ArgMask, 0, pool.ArgMask.Length);
            stream.Write(MemoryMarshal.AsBytes(pool.Reward.AsSpan()));
            stream.Write(pool.Alive, 0, pool.Alive.Length);
            stream.Write(pool.Done, 0, pool.Done.Length);
            stream.Write(pool.Expert, 0, pool.Expert.Length);
            WriteJson(stream, pool.Finished);
            stream.Flush();
        }
    }
}
