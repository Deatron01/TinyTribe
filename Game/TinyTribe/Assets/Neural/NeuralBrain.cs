using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using Unity.InferenceEngine;
using UnityEngine;

namespace TinyTribe.Neural
{
    /// <summary>What ML/install_model.py writes beside a network: where it came from and the shapes it was trained with.</summary>
    public sealed class NeuralModelInfo
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("experiment")] public string Experiment;
        [JsonProperty("hidden")] public int Hidden;
        [JsonProperty("grid_size")] public int GridSize;
        [JsonProperty("channel_sizes")] public int[] ChannelSizes;
        [JsonProperty("vector_length")] public int VectorLength;
        [JsonProperty("action_count")] public int ActionCount;
        [JsonProperty("arg_count")] public int ArgCount;
    }

    /// <summary>
    /// A trained network playing villagers in the game (Inference Engine, CPU). Every villager shares
    /// the weights and has a memory of its own, the LSTM state. Each tick a villager's view goes in and
    /// scores for every action and argument come out; it picks among what the engine allows, by chance
    /// in proportion to the scores, or always the highest when greedy.
    ///
    /// Networks are put into Assets/Resources/Models by ML/install_model.py: name.onnx and name.json.
    /// </summary>
    public sealed class NeuralBrain : IBrain, IDisposable
    {
        const string Folder = "Models/";

        public NeuralModelInfo Info { get; }
        public bool Greedy;

        readonly Worker worker;
        readonly Rng rng;
        readonly int planeCount, tiles;
        readonly int[] planeOffset;
        readonly float[] planes, vector;
        readonly byte[] grid;
        readonly List<float[]> memoryH = new List<float[]>(), memoryC = new List<float[]>();
        readonly TensorShape planesShape, vectorShape, stateShape;
        bool[] mask;
        byte[] table;
        float[] odds;
        Simulation bound;

        /// <summary>The names of the networks installed in the project.</summary>
        public static string[] Installed()
        {
            var names = new List<string>();
            foreach (var asset in Resources.LoadAll<ModelAsset>("Models")) names.Add(asset.name);
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        /// <summary>Loads Resources/Models/name. Throws with a plain explanation when it is missing or made for other data.</summary>
        public static NeuralBrain Load(string name, ulong seed, bool greedy = false)
        {
            var asset = Resources.Load<ModelAsset>(Folder + name);
            var about = Resources.Load<TextAsset>(Folder + name);
            if (asset == null || about == null)
                throw new InvalidOperationException("No trained network '" + name + "' in Assets/Resources/Models. Install one with ML/install_model.py.");
            return new NeuralBrain(ModelLoader.Load(asset), JsonConvert.DeserializeObject<NeuralModelInfo>(about.text), seed, greedy);
        }

        public NeuralBrain(Model model, NeuralModelInfo info, ulong seed, bool greedy = false)
        {
            Info = info;
            Greedy = greedy;
            rng = new Rng(seed ^ 0x9E3779B97F4A7C15UL);
            worker = new Worker(model, BackendType.CPU);
            tiles = info.GridSize * info.GridSize;
            planeOffset = new int[info.ChannelSizes.Length];
            foreach (int size in info.ChannelSizes) planeCount += size;
            for (int channel = 1; channel < planeOffset.Length; channel++) planeOffset[channel] = planeOffset[channel - 1] + info.ChannelSizes[channel - 1];
            planes = new float[planeCount * tiles];
            vector = new float[info.VectorLength];
            grid = new byte[tiles * info.ChannelSizes.Length];
            planesShape = new TensorShape(1, planeCount, info.GridSize, info.GridSize);
            vectorShape = new TensorShape(1, info.VectorLength);
            stateShape = new TensorShape(1, 1, info.Hidden);
        }

        /// <summary>The network was trained on one layout of the observation; a workbook that has moved on needs a new network.</summary>
        void Bind(Simulation sim)
        {
            if (ReferenceEquals(sim, bound)) return;
            var layout = sim.Observation;
            bool same = layout.VectorLength == Info.VectorLength && layout.GridSize == Info.GridSize && layout.ActionCount == Info.ActionCount
                        && layout.ArgCount == Info.ArgCount && layout.ChannelSizes.Length == Info.ChannelSizes.Length;
            for (int channel = 0; same && channel < layout.ChannelSizes.Length; channel++) same = layout.ChannelSizes[channel] == Info.ChannelSizes[channel];
            if (!same)
                throw new InvalidOperationException("The network '" + Info.Name + "' (" + Info.Experiment + ") was trained for another observation than this game's data gives. Train or install one for the current workbook.");
            bound = sim;
            mask = new bool[layout.ActionCount];
            table = new byte[layout.ActionCount * layout.ArgCount];
            odds = new float[Math.Max(layout.ActionCount, layout.ArgCount)];
            memoryH.Clear();
            memoryC.Clear();
        }

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            Bind(sim);
            while (memoryH.Count <= agentIndex)
            {
                memoryH.Add(new float[Info.Hidden]);
                memoryC.Add(new float[Info.Hidden]);
            }
            sim.Observe(agentIndex, grid, vector);
            sim.GetMasks(agentIndex, mask, table);

            // The grid's integers as one plane per value, planes first, the way the network was trained.
            Array.Clear(planes, 0, planes.Length);
            int channels = Info.ChannelSizes.Length;
            for (int tile = 0; tile < tiles; tile++)
                for (int channel = 0; channel < channels; channel++)
                {
                    int value = Math.Min(grid[tile * channels + channel], Info.ChannelSizes[channel] - 1);
                    planes[(planeOffset[channel] + value) * tiles + tile] = 1f;
                }

            float[] actionScores, argScores;
            using (var planesIn = new Tensor<float>(planesShape, planes))
            using (var vectorIn = new Tensor<float>(vectorShape, vector))
            using (var hIn = new Tensor<float>(stateShape, memoryH[agentIndex]))
            using (var cIn = new Tensor<float>(stateShape, memoryC[agentIndex]))
            {
                worker.SetInput("planes", planesIn);
                worker.SetInput("vector", vectorIn);
                worker.SetInput("h_in", hIn);
                worker.SetInput("c_in", cIn);
                worker.Schedule();
                actionScores = Download("action_logits");
                argScores = Download("arg_logits");
                memoryH[agentIndex] = Download("h_out");
                memoryC[agentIndex] = Download("c_out");
            }

            var layout = sim.Observation;
            int action = Pick(actionScores, 0, layout.ActionCount, i => mask[i]);
            if (action < 0) return new AgentAction(sim.Actions.Wait);
            int row = action * layout.ArgCount;
            int arg = Pick(argScores, row, layout.ArgCount, i => table[row + i] != 0);
            return new AgentAction(action, Math.Max(0, arg));
        }

        float[] Download(string output)
        {
            using (var tensor = (worker.PeekOutput(output) as Tensor<float>).ReadbackAndClone())
                return tensor.DownloadToArray();
        }

        /// <summary>One of the allowed entries: the highest-scoring when greedy, otherwise by chance in proportion to exp(score). -1 when none is allowed.</summary>
        int Pick(float[] scores, int from, int count, Func<int, bool> allowed)
        {
            int best = -1;
            float top = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!allowed(i) || scores[from + i] <= top) continue;
                top = scores[from + i];
                best = i;
            }
            if (best < 0 || Greedy) return best;

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                odds[i] = allowed(i) ? (float)Math.Exp(scores[from + i] - top) : 0f;
                total += odds[i];
            }
            float roll = rng.NextFloat() * total;
            for (int i = 0; i < count; i++)
            {
                roll -= odds[i];
                if (odds[i] > 0f && roll <= 0f) return i;
            }
            return best;
        }

        public void Dispose() => worker.Dispose();
    }
}
