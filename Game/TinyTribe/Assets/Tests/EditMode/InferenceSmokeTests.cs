using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace TinyTribe.Tests
{
    /// <summary>
    /// Risk test from the tech plan: a CNN + LSTM policy exported from PyTorch must give the
    /// same outputs in the Inference Engine as in onnxruntime, with the LSTM state carried
    /// across ticks. Model and reference are produced by ML/make_smoke_model.py.
    /// </summary>
    public class InferenceSmokeTests
    {
        const string ModelPath = "Assets/Models/smoke_lstm.onnx";
        const float Tolerance = 1e-4f;

        class Step
        {
            public float[] grid;
            public float[] vector;
            public float[] action_logits;
            public float[] arg_logits;
            public float[] h_out;
        }

        class Reference
        {
            public int[] grid_shape;
            public int vector_len;
            public int hidden;
            public List<Step> steps;
        }

        [TestCase(BackendType.CPU)]
        [TestCase(BackendType.GPUCompute)]
        public void Cnn_lstm_policy_matches_onnxruntime(BackendType backend)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.IsNotNull(asset, ModelPath + " not found. Run ML/make_smoke_model.py.");
            var referencePath = Path.Combine(Application.streamingAssetsPath, "Tests", "smoke_lstm_reference.json");
            var reference = JsonConvert.DeserializeObject<Reference>(File.ReadAllText(referencePath));

            var model = ModelLoader.Load(asset);
            using var worker = new Worker(model, backend);

            var stateShape = new TensorShape(1, 1, reference.hidden);
            var h = new float[reference.hidden];
            var c = new float[reference.hidden];

            for (int i = 0; i < reference.steps.Count; i++)
            {
                var step = reference.steps[i];
                using var grid = new Tensor<float>(new TensorShape(reference.grid_shape), step.grid);
                using var vector = new Tensor<float>(new TensorShape(1, reference.vector_len), step.vector);
                using var hIn = new Tensor<float>(stateShape, h);
                using var cIn = new Tensor<float>(stateShape, c);

                worker.SetInput("grid", grid);
                worker.SetInput("vector", vector);
                worker.SetInput("h_in", hIn);
                worker.SetInput("c_in", cIn);
                worker.Schedule();

                var action = Download(worker, "action_logits");
                var arg = Download(worker, "arg_logits");
                h = Download(worker, "h_out");
                c = Download(worker, "c_out");

                AssertClose(step.action_logits, action, "action_logits step " + i);
                AssertClose(step.arg_logits, arg, "arg_logits step " + i);
                AssertClose(step.h_out, h, "h_out step " + i);
            }
        }

        static float[] Download(Worker worker, string output)
        {
            using var tensor = (worker.PeekOutput(output) as Tensor<float>).ReadbackAndClone();
            return tensor.DownloadToArray();
        }

        static void AssertClose(float[] expected, float[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what + " length");
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], actual[i], Tolerance, what + " [" + i + "]");
        }
    }
}
