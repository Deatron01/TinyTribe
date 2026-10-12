using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>An event as events.bin holds it: what happened, and the tick it happened on.</summary>
    public struct LoggedEvent
    {
        public long Tick;
        public SimEvent Event;
    }

    /// <summary>
    /// Reads a recorded run back (rule R43). Replay builds the world again from the run's setup and
    /// feeds it the recorded actions, checking the state hashes on the way: the same run, tick for
    /// tick, to stop at any moment and look at, or to work out what was never written down.
    /// </summary>
    public static class RunReplay
    {
        public static RunFile Read(string folder) =>
            JsonConvert.DeserializeObject<RunFile>(File.ReadAllText(Path.Combine(folder, "run.json"), Encoding.UTF8));

        static BinaryReader Open(string folder, string name, string magic)
        {
            var reader = new BinaryReader(new FileStream(Path.Combine(folder, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16));
            string found = Encoding.ASCII.GetString(reader.ReadBytes(magic.Length));
            if (found == magic) return reader;
            reader.Dispose();
            throw new InvalidDataException(name + " does not start with " + magic);
        }

        public static List<LoggedEvent> ReadEvents(string folder)
        {
            var events = new List<LoggedEvent>();
            using (var reader = Open(folder, "events.bin", RunRecorder.EventsMagic))
            {
                long count = (reader.BaseStream.Length - reader.BaseStream.Position) / RunRecorder.EventRecordBytes;
                for (long i = 0; i < count; i++)
                {
                    long tick = reader.ReadUInt32();
                    var type = (SimEventType)reader.ReadUInt16();
                    int agent = reader.ReadInt16(), item = reader.ReadInt16(), qty = reader.ReadInt32(), x = reader.ReadInt16(), y = reader.ReadInt16();
                    events.Add(new LoggedEvent { Tick = tick, Event = new SimEvent(type, agent, item, qty, x, y) });
                }
            }
            return events;
        }

        static Dictionary<long, ulong> ReadHashes(string folder)
        {
            var hashes = new Dictionary<long, ulong>();
            if (!File.Exists(Path.Combine(folder, "hashes.bin"))) return hashes;
            using (var reader = Open(folder, "hashes.bin", RunRecorder.HashesMagic))
                while (reader.BaseStream.Length - reader.BaseStream.Position >= 12)
                {
                    long tick = reader.ReadUInt32();
                    hashes[tick] = reader.ReadUInt64();
                }
            return hashes;
        }

        /// <summary>
        /// Plays the run again up to untilTick (the whole run by default) and returns the simulation as
        /// it stands then. beforeTick is called with the world as the villagers saw it and what each of
        /// them is about to do; afterTick after the tick, with its events and rewards still in place.
        /// problem says what went wrong, or is null: a replay that leaves the recorded run (other data,
        /// other rules, an edited log) is stopped at the first state hash that differs.
        /// </summary>
        public static Simulation Replay(GameData data, string folder, out string problem, long untilTick = long.MaxValue,
            Action<Simulation> afterTick = null, Action<Simulation, AgentAction[]> beforeTick = null)
        {
            problem = null;
            var run = Read(folder);
            if (run.Setup == null || !run.Setup.Replayable)
            {
                problem = "this run was not started from a generated world at tick 0, so there is nothing to rebuild it from";
                return null;
            }
            string otherData = run.Data != null && run.Data.WorkbookSha256 != data.WorkbookSha256
                ? " (it was recorded with other data: " + run.Data.Workbook + " " + Short(run.Data.WorkbookSha256) + ", this is " + Short(data.WorkbookSha256) + ")"
                : "";

            var sim = Simulation.Create(data, run.Setup.ToOptions());
            sim.RewardShapingScale = run.Setup.RewardShapingScale;
            var hashes = ReadHashes(folder);
            var actions = new AgentAction[sim.Agents.Count];
            using (var reader = Open(folder, "actions.bin", RunRecorder.ActionsMagic))
            {
                var stream = reader.BaseStream;
                while (sim.Tick < untilTick && stream.Position < stream.Length)
                {
                    int count = reader.ReadByte();
                    if (count != sim.Agents.Count)
                    {
                        problem = "tick " + sim.Tick + ": the log has " + count + " villagers, the world has " + sim.Agents.Count + otherData;
                        return sim;
                    }
                    if (stream.Length - stream.Position < 2 * count) break;
                    // A child was born: one more chooses from now on.
                    if (actions.Length != count) actions = new AgentAction[count];
                    for (int i = 0; i < count; i++)
                    {
                        int id = reader.ReadByte();
                        actions[i] = new AgentAction(id, reader.ReadByte());
                    }
                    beforeTick?.Invoke(sim, actions);
                    sim.Step(actions);
                    afterTick?.Invoke(sim);
                    if (hashes.TryGetValue(sim.Tick, out ulong recorded) && recorded != sim.ComputeStateHash())
                    {
                        problem = "the replay is no longer the recorded run at tick " + sim.Tick + ": the state hash differs" + otherData;
                        return sim;
                    }
                }
            }

            if (run.Result != null && sim.Tick == run.Result.Ticks && run.Result.FinalStateHash != sim.ComputeStateHash().ToString(System.Globalization.CultureInfo.InvariantCulture))
                problem = "the replay ends at tick " + sim.Tick + " in another state than the recorded run" + otherData;
            return sim;
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(8, sha.Length));
    }
}
