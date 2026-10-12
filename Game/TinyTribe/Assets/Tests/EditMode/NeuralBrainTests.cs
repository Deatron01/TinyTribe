using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Neural;
using TinyTribe.Sim.Core;
using Unity.InferenceEngine;
using UnityEngine;

namespace TinyTribe.Tests
{
    /// <summary>
    /// A trained network in the game. These run against whatever ML/install_model.py last put into
    /// Assets/Resources/Models and are skipped while nothing is installed.
    /// </summary>
    public class NeuralBrainTests
    {
        const float Tolerance = 2e-3f;

        class Step
        {
            public float[] planes;
            public float[] vector;
            public float[] action_logits;
            public float[] arg_logits;
            public float[] h_out;
        }

        class Reference
        {
            public List<Step> steps;
        }

        static string Model()
        {
            var installed = NeuralBrain.Installed();
            if (installed.Length == 0) Assert.Ignore("No trained network is installed. Train one and run ML/install_model.py.");
            return installed.Contains("villager") ? "villager" : installed[0];
        }

        [Test]
        public void The_game_gets_the_same_scores_out_of_the_network_as_the_trainer_did()
        {
            string name = Model();
            var info = JsonConvert.DeserializeObject<NeuralModelInfo>(Resources.Load<TextAsset>("Models/" + name).text);
            var reference = JsonConvert.DeserializeObject<Reference>(Resources.Load<TextAsset>("Models/" + name + ".reference").text);
            int planes = info.ChannelSizes.Sum();
            using var worker = new Worker(ModelLoader.Load(Resources.Load<ModelAsset>("Models/" + name)), BackendType.CPU);
            var state = new TensorShape(1, 1, info.Hidden);
            float[] h = new float[info.Hidden], c = new float[info.Hidden];

            for (int i = 0; i < reference.steps.Count; i++)
            {
                var step = reference.steps[i];
                using var planesIn = new Tensor<float>(new TensorShape(1, planes, info.GridSize, info.GridSize), step.planes);
                using var vectorIn = new Tensor<float>(new TensorShape(1, info.VectorLength), step.vector);
                using var hIn = new Tensor<float>(state, h);
                using var cIn = new Tensor<float>(state, c);
                worker.SetInput("planes", planesIn);
                worker.SetInput("vector", vectorIn);
                worker.SetInput("h_in", hIn);
                worker.SetInput("c_in", cIn);
                worker.Schedule();

                Close(step.action_logits, Download(worker, "action_logits"), "action scores, tick " + i);
                Close(step.arg_logits, Download(worker, "arg_logits"), "argument scores, tick " + i);
                h = Download(worker, "h_out");
                c = Download(worker, "c_out");
                Close(step.h_out, h, "memory, tick " + i);
            }
        }

        static float[] Download(Worker worker, string output)
        {
            using var tensor = (worker.PeekOutput(output) as Tensor<float>).ReadbackAndClone();
            return tensor.DownloadToArray();
        }

        static void Close(float[] expected, float[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], actual[i], Tolerance, what + " [" + i + "]");
        }

        [Test]
        public void A_network_plays_a_village_asking_only_for_what_is_possible()
        {
            string name = Model();
            var setup = new GameSetup { Brains = BrainKind.Neural, Model = name, Record = false };
            setup.Sim.Seed = 21;
            using var brain = NeuralBrain.Load(name, setup.Sim.Seed);
            var runner = SimRunner.Create(Kit.Data, setup, brain);
            var sim = runner.Sim;
            var stats = new RunStats(Kit.Data, sim);
            runner.Stats = stats;
            var seen = new HashSet<int>();

            for (int t = 0; t < 200; t++)
            {
                runner.StepOnce();
                foreach (var agent in sim.Agents) seen.Add(agent.LastAction.Id);
            }

            // Everyone chooses on the state before the tick; what a villager ahead of it did can still spoil a move.
            Assert.LessOrEqual(stats.Get("invalid_actions"), 200 * sim.Agents.Count / 20, "next to nothing it asked for was refused");
            Assert.GreaterOrEqual(seen.Count, 4, "it does more than one or two things");
        }

        [Test]
        public void A_network_trained_for_other_data_says_so_instead_of_playing_nonsense()
        {
            string name = Model();
            var info = JsonConvert.DeserializeObject<NeuralModelInfo>(Resources.Load<TextAsset>("Models/" + name).text);
            info.VectorLength += 3;
            using var brain = new NeuralBrain(ModelLoader.Load(Resources.Load<ModelAsset>("Models/" + name)), info, 1);
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3 });

            var problem = Assert.Throws<System.InvalidOperationException>(() => brain.Decide(sim, 0));

            StringAssert.Contains("another observation", problem.Message);
        }

        [Test]
        public void Without_a_brain_handed_in_a_network_game_cannot_be_made()
        {
            var setup = new GameSetup { Brains = BrainKind.Neural };

            Assert.Throws<System.InvalidOperationException>(() => SimRunner.Create(Kit.Data, setup));
        }
    }
}
