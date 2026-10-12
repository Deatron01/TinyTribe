using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>
    /// A recorded run turned into training data: what every villager saw on every tick, what it could
    /// do, what it chose and what it earned. Worked out by replaying the run, so no run has to store it.
    /// </summary>
    public class DatasetExportTests
    {
        const int Ticks = 150;
        string folder;
        SimRunner runner;
        RunRecorder recorder;
        string episode;

        [OneTimeSetUp]
        public void RecordAndExport()
        {
            folder = Path.Combine(Path.GetTempPath(), "tinytribe-tests", Guid.NewGuid().ToString("N"));
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = 7;
            runner = SimRunner.Create(Kit.Data, setup);
            recorder = RunRecorder.Start(Path.Combine(folder, "runs"), Kit.Data, runner.Sim, setup);
            runner.Recorder = recorder;
            for (int t = 0; t < Ticks; t++) runner.StepOnce();
            recorder.Finish("stopped");

            episode = DatasetExporter.Export(Kit.Data, recorder.Folder, Path.Combine(folder, "dataset"), out string problem);
            Assert.IsNull(problem);
        }

        [OneTimeTearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        /// <summary>The bytes of a .npy file after its header, having checked the header says what is expected.</summary>
        byte[] Array(string name, string type, params int[] shape)
        {
            var bytes = File.ReadAllBytes(Path.Combine(episode, name + ".npy"));
            Assert.AreEqual(0x93, bytes[0]);
            Assert.AreEqual("NUMPY", Encoding.ASCII.GetString(bytes, 1, 5));
            int headerLength = BitConverter.ToUInt16(bytes, 8);
            string header = Encoding.ASCII.GetString(bytes, 10, headerLength);
            StringAssert.Contains("'descr': '" + type + "'", header);
            StringAssert.Contains("'fortran_order': False", header);
            StringAssert.Contains("'shape': (" + string.Join(", ", shape) + (shape.Length == 1 ? "," : "") + ")", header);
            Assert.AreEqual(0, (10 + headerLength) % 64, "numpy wants the data to start on a 64-byte boundary");
            return bytes.Skip(10 + headerLength).ToArray();
        }

        int Agents => runner.Sim.Agents.Count;

        [Test]
        public void An_episode_is_a_folder_of_arrays_one_row_per_villager_per_tick()
        {
            var layout = runner.Sim.Observation;
            int maskBytes = (layout.ActionCount * layout.ArgCount + 7) / 8;

            Assert.AreEqual(recorder.RunId, Path.GetFileName(episode));
            Assert.AreEqual(Ticks * Agents * layout.GridLength, Array("grid", "|u1", Ticks, Agents, layout.GridLength).Length);
            Assert.AreEqual(Ticks * Agents * layout.VectorLength * 4, Array("vector", "<f4", Ticks, Agents, layout.VectorLength).Length);
            Assert.AreEqual(Ticks * Agents * layout.ActionCount, Array("action_mask", "|u1", Ticks, Agents, layout.ActionCount).Length);
            Assert.AreEqual(Ticks * Agents * maskBytes, Array("arg_mask", "|u1", Ticks, Agents, maskBytes).Length);
            Assert.AreEqual(Ticks * Agents * 2, Array("action", "|u1", Ticks, Agents, 2).Length);
            Assert.AreEqual(Ticks * Agents * 2 * 4, Array("reward", "<f4", Ticks, Agents, 2).Length);
            Assert.AreEqual(Ticks * Agents, Array("alive", "|u1", Ticks, Agents).Length);
        }

        [Test]
        public void The_episode_says_what_it_is_and_how_to_read_it()
        {
            var about = JObject.Parse(File.ReadAllText(Path.Combine(episode, "episode.json")));
            var layout = runner.Sim.Observation;

            Assert.AreEqual(recorder.RunId, (string)about["run_id"]);
            Assert.AreEqual(7, (int)about["seed"]);
            Assert.AreEqual("Survival", (string)about["brains"]);
            Assert.AreEqual(Ticks, (int)about["ticks"]);
            Assert.AreEqual(Agents, (int)about["agents"]);
            Assert.AreEqual(Kit.Data.WorkbookSha256, (string)about["workbook_sha256"]);
            Assert.AreEqual(layout.VectorLength, (int)about["layout"]["vector_length"]);
            Assert.AreEqual(layout.GridSize, (int)about["layout"]["grid_size"]);
            CollectionAssert.AreEqual(layout.ChannelSizes, about["layout"]["channel_sizes"].Select(v => (int)v).ToArray());
            Assert.AreEqual(layout.Fields["needs"].Start, (int)about["layout"]["fields"]["needs"][0]);
            Assert.AreEqual(layout.Fields["village works"].Length, (int)about["layout"]["fields"]["village works"][1]);
            Assert.AreEqual(Agents, about["villagers"].Count());
            Assert.AreEqual(Kit.Data.Actions.Count, about["actions"].Count(), "the names of the actions, for reading results");
        }

        [Test]
        public void The_actions_are_the_ones_that_were_recorded()
        {
            var actions = Array("action", "|u1", Ticks, Agents, 2);
            var logged = File.ReadAllBytes(Path.Combine(recorder.Folder, "actions.bin"));
            int record = 1 + 2 * Agents;

            for (int tick = 0; tick < Ticks; tick++)
                for (int agent = 0; agent < Agents; agent++)
                {
                    Assert.AreEqual(logged[8 + tick * record + 1 + agent * 2], actions[(tick * Agents + agent) * 2], "action at tick " + tick);
                    Assert.AreEqual(logged[8 + tick * record + 2 + agent * 2], actions[(tick * Agents + agent) * 2 + 1], "argument at tick " + tick);
                }
        }

        [Test]
        public void A_row_is_what_the_villager_saw_before_it_chose()
        {
            const int tick = 97, agent = 3;
            var layout = runner.Sim.Observation;
            var before = RunReplay.Replay(Kit.Data, recorder.Folder, out _, untilTick: tick);
            var grid = new byte[layout.GridLength];
            var vector = new float[layout.VectorLength];
            var mask = new bool[layout.ActionCount];
            var table = new byte[layout.ActionCount * layout.ArgCount];
            before.Observe(agent, grid, vector);
            before.GetMasks(agent, mask, table);
            int row = tick * Agents + agent;

            var grids = Array("grid", "|u1", Ticks, Agents, layout.GridLength);
            var vectors = Array("vector", "<f4", Ticks, Agents, layout.VectorLength);
            var masks = Array("action_mask", "|u1", Ticks, Agents, layout.ActionCount);
            int maskBytes = (layout.ActionCount * layout.ArgCount + 7) / 8;
            var tables = Array("arg_mask", "|u1", Ticks, Agents, maskBytes);

            CollectionAssert.AreEqual(grid, grids.Skip(row * layout.GridLength).Take(layout.GridLength).ToArray());
            for (int i = 0; i < layout.VectorLength; i++)
                Assert.AreEqual(vector[i], BitConverter.ToSingle(vectors, (row * layout.VectorLength + i) * 4), "vector[" + i + "]");
            for (int i = 0; i < layout.ActionCount; i++) Assert.AreEqual(mask[i] ? 1 : 0, masks[row * layout.ActionCount + i]);
            // Packed the way numpy unpacks: the first flag is the highest bit of the first byte.
            for (int i = 0; i < table.Length; i++)
                Assert.AreEqual(table[i], (tables[row * maskBytes + i / 8] >> (7 - i % 8)) & 1, "argument flag " + i);
        }

        [Test]
        public void The_action_chosen_was_always_one_the_masks_allowed()
        {
            var layout = runner.Sim.Observation;
            var actions = Array("action", "|u1", Ticks, Agents, 2);
            var masks = Array("action_mask", "|u1", Ticks, Agents, layout.ActionCount);
            int maskBytes = (layout.ActionCount * layout.ArgCount + 7) / 8;
            var tables = Array("arg_mask", "|u1", Ticks, Agents, maskBytes);
            var alive = Array("alive", "|u1", Ticks, Agents);
            int refused = 0;

            for (int row = 0; row < Ticks * Agents; row++)
            {
                if (alive[row] == 0) continue;
                int action = actions[row * 2], arg = actions[row * 2 + 1];
                int flag = action * layout.ArgCount + arg;
                bool allowed = masks[row * layout.ActionCount + action] != 0 && ((tables[row * maskBytes + flag / 8] >> (7 - flag % 8)) & 1) != 0;
                if (!allowed) refused++;
            }

            // The bots decide on the state before the tick, like the masks; what a villager ahead of it in the tick did can still spoil a move.
            Assert.LessOrEqual(refused, Ticks * Agents / 50, "almost nothing the bots chose was impossible when they chose it");
        }

        [Test]
        public void The_rewards_add_up_to_what_the_run_counted()
        {
            var rewards = Array("reward", "<f4", Ticks, Agents, 2);
            double total = 0;
            for (int i = 0; i < Ticks * Agents * 2; i++) total += BitConverter.ToSingle(rewards, i * 4);

            Assert.AreEqual(recorder.Stats.Get("reward_total"), total, 1e-2);
        }

        [Test]
        public void A_run_that_cannot_be_replayed_is_not_exported()
        {
            string runs = Path.Combine(folder, "custom");
            var sim = Kit.NewSim(12, 9);
            sim.AddAgent(4, 4, Kit.Neutral);
            var custom = RunRecorder.Start(runs, Kit.Data, sim, null);
            var actions = new[] { new AgentAction(sim.Actions.Wait) };
            sim.Step(actions);
            custom.AfterTick(actions);
            custom.Finish("stopped");

            string exported = DatasetExporter.Export(Kit.Data, custom.Folder, Path.Combine(folder, "dataset2"), out string problem);

            Assert.IsNull(exported);
            Assert.IsNotNull(problem);
        }
    }
}
