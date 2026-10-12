using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>
    /// Turns a recorded run into training data by playing it again (rule R43): for every villager on
    /// every tick, what it saw, what it could do, what it chose and what it earned. Written as plain
    /// NumPy arrays, ticks x villagers x values, which Python opens without reading them into memory.
    /// </summary>
    public static class DatasetExporter
    {
        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        /// <summary>
        /// Exports the run into outFolder/run_id and returns that folder; null, with the reason in
        /// problem, when the run cannot be replayed or the replay leaves the recorded run.
        /// </summary>
        public static string Export(GameData data, string runFolder, string outFolder, out string problem)
        {
            var run = RunReplay.Read(runFolder);
            if (run.Setup == null || !run.Setup.Replayable)
            {
                problem = "this run was not started from a generated world at tick 0, so it cannot be replayed";
                return null;
            }
            // A village may grow: every tick's record says how many villagers chose in it.
            long ticks = 0;
            int agents = run.Setup.Agents;
            using (var log = new BinaryReader(File.OpenRead(Path.Combine(runFolder, "actions.bin"))))
            {
                long length = log.BaseStream.Length;
                log.BaseStream.Position = 8;
                while (log.BaseStream.Position < length)
                {
                    int count = log.ReadByte();
                    if (log.BaseStream.Position + 2 * count > length) break;
                    log.BaseStream.Position += 2 * count;
                    agents = Math.Max(agents, count);
                    ticks++;
                }
            }
            string folder = Path.Combine(outFolder, run.RunId);
            Directory.CreateDirectory(folder);

            var sim = Simulation.Create(data, run.Setup.ToOptions());
            var layout = sim.Observation;
            int maskBytes = (layout.ActionCount * layout.ArgCount + 7) / 8;
            var grid = new byte[layout.GridLength];
            var vector = new float[layout.VectorLength];
            var vectorBytes = new byte[layout.VectorLength * 4];
            var mask = new bool[layout.ActionCount];
            var maskRow = new byte[layout.ActionCount];
            var table = new byte[layout.ActionCount * layout.ArgCount];
            var packed = new byte[maskBytes];
            var rewardBytes = new byte[8];
            var parts = new float[2];

            using (var grids = Npy(folder, "grid", "|u1", ticks, agents, layout.GridLength))
            using (var vectors = Npy(folder, "vector", "<f4", ticks, agents, layout.VectorLength))
            using (var masks = Npy(folder, "action_mask", "|u1", ticks, agents, layout.ActionCount))
            using (var tables = Npy(folder, "arg_mask", "|u1", ticks, agents, maskBytes))
            using (var actions = Npy(folder, "action", "|u1", ticks, agents, 2))
            using (var rewards = Npy(folder, "reward", "<f4", ticks, agents, 2))
            using (var alive = Npy(folder, "alive", "|u1", ticks, agents))
            {
                // What the villagers see is written as each tick is about to be played; what they earn, as it has been.
                var replayed = RunReplay.Replay(data, runFolder, out problem,
                    beforeTick: (before, chosen) =>
                    {
                        for (int i = 0; i < agents; i++)
                        {
                            // Not born yet is written like dead: nothing seen, nothing chosen.
                            bool living = i < before.Agents.Count && before.Agents[i].Alive;
                            if (living)
                            {
                                before.Observe(i, grid, vector);
                                before.GetMasks(i, mask, table);
                                Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);
                                for (int a = 0; a < maskRow.Length; a++) maskRow[a] = (byte)(mask[a] ? 1 : 0);
                                Array.Clear(packed, 0, packed.Length);
                                for (int flag = 0; flag < table.Length; flag++)
                                    if (table[flag] != 0) packed[flag >> 3] |= (byte)(0x80 >> (flag & 7));
                            }
                            else
                            {
                                Array.Clear(grid, 0, grid.Length);
                                Array.Clear(vectorBytes, 0, vectorBytes.Length);
                                Array.Clear(maskRow, 0, maskRow.Length);
                                Array.Clear(packed, 0, packed.Length);
                            }
                            grids.Write(grid, 0, grid.Length);
                            vectors.Write(vectorBytes, 0, vectorBytes.Length);
                            masks.Write(maskRow, 0, maskRow.Length);
                            tables.Write(packed, 0, packed.Length);
                            actions.WriteByte((byte)(i < chosen.Length ? chosen[i].Id : 0));
                            actions.WriteByte((byte)(i < chosen.Length ? chosen[i].Arg : 0));
                            alive.WriteByte((byte)(living ? 1 : 0));
                        }
                    },
                    afterTick: after =>
                    {
                        for (int i = 0; i < agents; i++)
                        {
                            after.RewardPartsOf(i, out parts[0], out parts[1]);
                            Buffer.BlockCopy(parts, 0, rewardBytes, 0, 8);
                            rewards.Write(rewardBytes, 0, 8);
                        }
                    });
                if (problem != null)
                {
                    problem = "not exported: " + problem;
                }
                else if (replayed.Tick != ticks) problem = "not exported: the replay stopped at tick " + replayed.Tick + " of " + ticks;
            }
            if (problem != null)
            {
                Directory.Delete(folder, true);
                return null;
            }

            var fields = new Dictionary<string, int[]>();
            foreach (var field in layout.Fields) fields[field.Key] = new[] { field.Value.Start, field.Value.Length };
            var actionNames = new List<string>();
            var takesArgument = new List<bool>();
            foreach (var action in data.Actions)
            {
                actionNames.Add(action.Name);
                takesArgument.Add(sim.TakesArgument(action.Id));
            }
            var about = new Dictionary<string, object>
            {
                ["run_id"] = run.RunId,
                ["seed"] = run.Setup.Seed,
                ["brains"] = run.Setup.Brains,
                ["ticks"] = ticks,
                ["agents"] = agents,
                ["workbook"] = data.Workbook,
                ["workbook_sha256"] = data.WorkbookSha256,
                ["setup"] = run.Setup,
                ["villagers"] = run.Villagers,
                ["actions"] = actionNames,
                ["takes_argument"] = takesArgument,
                ["layout"] = new Dictionary<string, object>
                {
                    ["grid_size"] = layout.GridSize,
                    ["grid_channels"] = layout.GridChannels,
                    ["channel_sizes"] = layout.ChannelSizes,
                    ["vector_length"] = layout.VectorLength,
                    ["action_count"] = layout.ActionCount,
                    ["arg_count"] = layout.ArgCount,
                    ["item_scale"] = layout.ItemScale,
                    ["role_scale"] = layout.RoleScale,
                    ["fields"] = fields,
                },
                ["arrays"] = new Dictionary<string, string>
                {
                    ["grid"] = "uint8 [ticks, agents, grid_size * grid_size * grid_channels]: rows from the north, channels of a tile together",
                    ["vector"] = "float32 [ticks, agents, vector_length]: see layout.fields",
                    ["action_mask"] = "uint8 [ticks, agents, action_count]: 1 where the action was possible",
                    ["arg_mask"] = "uint8 [ticks, agents, ceil(action_count * arg_count / 8)]: bits, numpy.unpackbits order, action by action",
                    ["action"] = "uint8 [ticks, agents, 2]: the action chosen and its argument",
                    ["reward"] = "float32 [ticks, agents, 2]: earned on that tick for itself, and as one of the village",
                    ["alive"] = "uint8 [ticks, agents]: 0 from the tick after a villager died; its rows are zeros",
                },
            };
            File.WriteAllText(Path.Combine(folder, "episode.json"), JsonConvert.SerializeObject(about, Formatting.Indented), new UTF8Encoding(false));
            return folder;
        }

        /// <summary>A .npy file with its header written and the stream ready for the rows.</summary>
        static FileStream Npy(string folder, string name, string type, params long[] shape)
        {
            var stream = new FileStream(Path.Combine(folder, name + ".npy"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            var dims = new StringBuilder();
            foreach (long dim in shape) dims.Append(dim.ToString(Plain)).Append(", ");
            if (shape.Length > 1) dims.Length -= 2;
            else dims.Length -= 1;
            string header = "{'descr': '" + type + "', 'fortran_order': False, 'shape': (" + dims + "), }";
            // Magic, version and the length field are 10 bytes; the data has to start on a 64-byte boundary, the header ends in a newline.
            int padded = (10 + header.Length + 1 + 63) / 64 * 64 - 10;
            header = header.PadRight(padded - 1) + "\n";
            stream.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0, (byte)(padded & 0xFF), (byte)(padded >> 8) }, 0, 10);
            var text = Encoding.ASCII.GetBytes(header);
            stream.Write(text, 0, text.Length);
            return stream;
        }
    }
}
