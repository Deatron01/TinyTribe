using System.IO;
using NUnit.Framework;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.Tests
{
    public class GameDataTests
    {
        static string DataDir => Path.Combine(Application.streamingAssetsPath, "Data");

        [Test]
        public void Loads_and_cross_references_are_valid()
        {
            var data = GameData.Load(DataDir);
            var errors = data.Validate();
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Loads_through_a_custom_file_reader()
        {
            var asked = new System.Collections.Generic.List<string>();

            var data = GameData.Load(file =>
            {
                asked.Add(file);
                return File.ReadAllText(Path.Combine(DataDir, file));
            });

            CollectionAssert.Contains(asked, "items.json");
            Assert.AreEqual(57, data.Items.Count);
            Assert.IsEmpty(data.Validate());
        }

        [Test]
        public void Missing_data_file_is_reported_with_the_fix()
        {
            var error = Assert.Throws<FileNotFoundException>(() => GameData.Load(Path.Combine(DataDir, "no_such_folder")));

            StringAssert.Contains("export_data.py", error.Message);
        }

        [Test]
        public void Table_sizes_match_the_workbook()
        {
            var data = GameData.Load(DataDir);
            Assert.AreEqual(57, data.Items.Count);
            Assert.AreEqual(36, data.Recipes.Count);
            Assert.AreEqual(27, data.Buildings.Count);
            Assert.AreEqual(21, data.Actions.Count);
            Assert.AreEqual(68, data.ActionArgs.Count);
            Assert.AreEqual(6, data.Needs.Count);
        }

        [Test]
        public void Config_values_are_readable()
        {
            var data = GameData.Load(DataDir);
            Assert.AreEqual(240, data.ConfigInt("ticks_per_day"));
            Assert.AreEqual(195, data.ConfigInt("obs_vector_length"));
            Assert.AreEqual(0.5f, data.ConfigFloat("bare_hand_speed_mult"));
            Assert.AreEqual("spring", data.ConfigString("start_season"));
        }
    }
}
