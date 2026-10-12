using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using UnityEngine;

namespace TinyTribe.Tests
{
    /// <summary>
    /// What a villager sees (Sim Observation): an 11 x 11 x 5 grid around it, a vector about itself and
    /// its village, and which actions and arguments are possible. This is all a network has to go on.
    /// </summary>
    public class ObservationTests
    {
        static ObservationLayout Layout => Kit.NewSim().Observation;

        static (byte[] grid, float[] vector) See(Simulation sim, Agent agent)
        {
            var layout = sim.Observation;
            var grid = new byte[layout.GridLength];
            var vector = new float[layout.VectorLength];
            sim.Observe(agent.Index, grid, vector);
            return (grid, vector);
        }

        /// <summary>The cell of the grid that shows the tile dx east and dy north of the villager.</summary>
        static byte Cell(Simulation sim, byte[] grid, int dx, int dy, int channel)
        {
            var layout = sim.Observation;
            int half = layout.GridSize / 2;
            int row = half - dy, column = half + dx;
            return grid[(row * layout.GridSize + column) * layout.GridChannels + channel];
        }

        // ------------------------------------------------------------------ layout

        [Test]
        public void The_vector_is_laid_out_as_the_workbook_says()
        {
            var layout = Layout;
            var sheet = JArray.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", "observation.json")));
            int checkedFields = 0;
            foreach (var row in sheet.Where(r => (string)r["block"] == "vector"))
            {
                string index = (string)row["index"], field = (string)row["field"];
                if (index == "length")
                {
                    Assert.AreEqual(int.Parse(field), layout.VectorLength);
                    continue;
                }
                var range = index.Split('-').Select(int.Parse).ToArray();
                Assert.IsTrue(layout.Fields.ContainsKey(field), "the engine does not know the field '" + field + "'");
                Assert.AreEqual(range[0], layout.Fields[field].Start, field);
                Assert.AreEqual(range.Length > 1 ? range[1] - range[0] + 1 : 1, layout.Fields[field].Length, field);
                checkedFields++;
            }
            Assert.AreEqual(layout.Fields.Count, checkedFields, "every field of the engine is in the sheet");
            Assert.AreEqual(Kit.Data.ConfigInt("obs_vector_length"), layout.VectorLength);
            Assert.AreEqual(layout.Fields.Values.Sum(f => f.Length), layout.VectorLength, "the fields leave no gaps");
        }

        [Test]
        public void The_grid_and_the_masks_have_the_sizes_of_the_workbook()
        {
            var layout = Layout;

            Assert.AreEqual(Kit.Data.ConfigInt("fov_size"), layout.GridSize);
            Assert.AreEqual(Kit.Data.ConfigInt("fov_channels"), layout.GridChannels);
            Assert.AreEqual(11 * 11 * 5, layout.GridLength);
            Assert.AreEqual(Kit.Data.ConfigInt("num_actions"), layout.ActionCount);
            Assert.AreEqual(Kit.Data.ConfigInt("num_action_args"), layout.ArgCount);
            CollectionAssert.AreEqual(new[]
            {
                Kit.Data.Terrain.Max(t => t.Int) + 1, Kit.Data.Animals.Max(a => a.Int) + 1, Kit.Data.ResourceNodes.Max(n => n.Int) + 1,
                Kit.Data.Buildings.Max(b => b.Int) + 1, 13,
            }, layout.ChannelSizes, "one more than the largest value of each channel");
        }

        // ------------------------------------------------------------------ the villager itself

        [Test]
        public void A_villager_sees_its_needs_and_stats()
        {
            var sim = Kit.NewSim();
            var stats = Kit.Neutral;
            stats.Str = 8;
            stats.Int = 2;
            stats.Empathy = 0.9f;
            var agent = sim.AddAgent(4, 4, stats);
            agent.Set(Need.Hunger, 40);
            agent.Set(Need.Temp, 75);
            var layout = sim.Observation;

            var (_, vector) = See(sim, agent);

            int needs = layout.Fields["needs"].Start, core = layout.Fields["core stats"].Start, social = layout.Fields["social stats"].Start;
            Assert.AreEqual(1f, vector[needs + (int)Need.Hp], 1e-6f);
            Assert.AreEqual(0.4f, vector[needs + (int)Need.Hunger], 1e-6f);
            Assert.AreEqual(0.75f, vector[needs + (int)Need.Temp], 1e-6f);
            Assert.AreEqual(0.8f, vector[core], 1e-6f);
            Assert.AreEqual(0.2f, vector[core + 2], 1e-6f);
            Assert.AreEqual(0.9f, vector[social + 1], 1e-6f);
        }

        [Test]
        public void A_villager_sees_what_it_carries_holds_and_wears()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "berry", 3);
            Village.Give(sim, agent, "wooden_spear", 1);
            Village.Do(sim, new AgentAction(sim.Actions.Equip, Kit.Item("wooden_spear")));
            var layout = sim.Observation;
            float items = Kit.Data.Items.Max(i => i.Int);

            var (_, vector) = See(sim, agent);

            int pack = layout.Fields["inventory"].Start, tool = layout.Fields["equipped tool"].Start;
            Assert.AreEqual(Kit.Item("berry") / items, vector[pack], 1e-6f);
            Assert.AreEqual(3f / Kit.Data.ItemById["berry"].MaxStack, vector[pack + 1], 1e-6f);
            Assert.AreEqual(0f, vector[pack + 2], "the next slot is empty");
            Assert.AreEqual(Kit.Item("wooden_spear") / items, vector[tool], 1e-6f);
            Assert.AreEqual(1f, vector[tool + 1], 1e-6f, "a new spear");
            Assert.AreEqual(0f, vector[layout.Fields["clothing"].Start]);
        }

        [Test]
        public void The_layout_says_how_to_read_items_and_jobs_back_out_of_the_numbers()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "wooden_spear", 1);
            Village.Give(sim, agent, "pine_wood", 2);
            agent.Role = Kit.Data.RoleInt("builder");
            var layout = sim.Observation;

            var (_, vector) = See(sim, agent);

            int pack = layout.Fields["inventory"].Start, role = layout.Fields["role"].Start;
            Assert.AreEqual(Kit.Data.Items.Max(i => i.Int), layout.ItemScale);
            Assert.AreEqual(Kit.Data.Roles.Max(r => r.Int), layout.RoleScale);
            Assert.AreEqual(Kit.Item("wooden_spear"), (int)Math.Round(vector[pack] * layout.ItemScale));
            Assert.AreEqual(Kit.Item("pine_wood"), (int)Math.Round(vector[pack + 2] * layout.ItemScale));
            Assert.AreEqual(Kit.Data.RoleInt("builder"), (int)Math.Round(vector[role] * layout.RoleScale));
        }

        [Test]
        public void A_villager_knows_the_hour_the_season_and_the_weather()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var layout = sim.Observation;
            sim.SetWeather("rain", 100);

            var (_, vector) = See(sim, agent);

            int time = layout.Fields["time of day"].Start, season = layout.Fields["season"].Start, weather = layout.Fields["weather"].Start;
            double angle = 2 * Math.PI * (sim.Hour * Kit.Data.ConfigInt("ticks_per_hour")) / Kit.Data.ConfigInt("ticks_per_day");
            Assert.AreEqual(Math.Sin(angle), vector[time], 1e-5);
            Assert.AreEqual(Math.Cos(angle), vector[time + 1], 1e-5);
            for (int i = 0; i < Kit.Data.Seasons.Count; i++) Assert.AreEqual(i == sim.Season.Order ? 1f : 0f, vector[season + i]);
            int rain = Kit.Data.Weather.ToList().FindIndex(w => w.Id == "rain");
            for (int i = 0; i < Kit.Data.Weather.Count; i++) Assert.AreEqual(i == rain ? 1f : 0f, vector[weather + i]);
        }

        [Test]
        public void A_villager_knows_its_job_and_the_way_to_the_village()
        {
            var sim = Kit.NewSim(40, 20);
            sim.World.SpawnX = 30;
            sim.World.SpawnY = 5;
            var agent = sim.AddAgent(10, 15, Kit.Neutral);
            sim.SetRole(agent, "miner");
            var layout = sim.Observation;

            var (_, vector) = See(sim, agent);

            Assert.AreEqual(Kit.Data.RoleInt("miner") / (float)Kit.Data.Roles.Max(r => r.Int), vector[layout.Fields["role"].Start], 1e-6f);
            int village = layout.Fields["village direction"].Start;
            Assert.AreEqual(20f / 40f, vector[village], 1e-6f);
            Assert.AreEqual(-10f / 20f, vector[village + 1], 1e-6f);
            Assert.AreEqual(0f, vector[layout.Fields["status flags"].Start + 3], "no home yet");
        }

        // ------------------------------------------------------------------ the grid

        [Test]
        public void The_grid_shows_the_ground_and_what_stands_on_it_north_at_the_top()
        {
            var sim = Kit.NewSim(30, 30);
            var agent = sim.AddAgent(10, 10, Kit.Neutral);
            sim.AddAgent(12, 10, Kit.Neutral);
            sim.World.PlaceNode(10, 12, Kit.Data.NodeById("berry_bush"));
            sim.World.SetTerrain(9, 10, Kit.Data.TerrainById("water"));
            sim.SpawnAnimal("wolf", 10, 7);
            sim.PlaceBuilding("basic_chest", 8, 8);

            var (grid, _) = See(sim, agent);

            Assert.AreEqual(Kit.Data.TerrainById("grass").Int, Cell(sim, grid, 0, 0, 0));
            Assert.AreEqual(Kit.Data.TerrainById("water").Int, Cell(sim, grid, -1, 0, 0), "west is to the left");
            Assert.AreEqual(Kit.Data.NodeById("berry_bush").Int, Cell(sim, grid, 0, 2, 2), "north is up");
            Assert.AreEqual(1, Cell(sim, grid, 2, 0, 1), "another villager");
            Assert.AreEqual(0, Cell(sim, grid, 0, 0, 1), "itself is not an entity it sees");
            Assert.AreEqual(Kit.Data.Animals.First(a => a.Id == "wolf").Int, Cell(sim, grid, 0, -3, 1));
            Assert.AreEqual(Kit.Data.BuildingById["basic_chest"].Int, Cell(sim, grid, -2, -2, 3));
            Assert.AreEqual(0, Cell(sim, grid, 3, 3, 2));
            Assert.AreEqual(0, Cell(sim, grid, 0, 0, 4), "nobody signals anything yet");
        }

        [Test]
        public void Beyond_the_edge_of_the_map_there_is_nothing()
        {
            var sim = Kit.NewSim(9, 9);
            var agent = sim.AddAgent(1, 7, Kit.Neutral);

            var (grid, _) = See(sim, agent);

            Assert.AreEqual(Kit.Data.TerrainById("out_of_bounds").Int, Cell(sim, grid, -2, 0, 0));
            Assert.AreEqual(Kit.Data.TerrainById("out_of_bounds").Int, Cell(sim, grid, 0, 2, 0));
            Assert.AreEqual(Kit.Data.TerrainById("grass").Int, Cell(sim, grid, -1, 1, 0), "the corner tile is still the map");
        }

        [Test]
        public void A_used_up_node_is_not_seen_as_a_resource()
        {
            var sim = Kit.NewSim(20, 20);
            var agent = sim.AddAgent(10, 10, Kit.Neutral);
            sim.World.PlaceNode(11, 10, Kit.Data.NodeById("berry_bush"));
            sim.World.NodeCharges[sim.World.Index(11, 10)] = 0;

            var (grid, _) = See(sim, agent);

            Assert.AreEqual(0, Cell(sim, grid, 1, 0, 2));
        }

        // ------------------------------------------------------------------ the compass

        [Test]
        public void The_compass_points_to_the_nearest_water_food_wood_and_stone()
        {
            var sim = Kit.NewSim(90, 90);
            var agent = sim.AddAgent(40, 40, Kit.Neutral);
            sim.World.SetTerrain(43, 40, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(43, 40, Kit.Data.NodeById("water_source"));
            sim.World.PlaceNode(38, 44, Kit.Data.NodeById("berry_bush"));
            sim.World.PlaceNode(50, 40, Kit.Data.NodeById("berry_bush"));
            sim.World.PlaceNode(40, 20, Kit.Data.NodeById("pine_tree"));
            float reach = Kit.Data.ConfigInt("obs_compass_radius");

            var (_, v) = See(sim, agent);

            int c = sim.Observation.Fields["compass"].Start;
            CollectionAssert.AreEqual(new[] { 1f, 0f, 1f - 3f / reach }, v.Skip(c).Take(3).ToArray(), "water, three tiles east");
            CollectionAssert.AreEqual(new[] { -0.5f, 1f, 1f - 4f / reach }, v.Skip(c + 3).Take(3).ToArray(), "the nearer of two bushes, north-north-west");
            CollectionAssert.AreEqual(new[] { 0f, -1f, 1f - 20f / reach }, v.Skip(c + 6).Take(3).ToArray(), "a tree far to the south");
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f }, v.Skip(c + 9).Take(3).ToArray(), "no rock anywhere");
        }

        [Test]
        public void The_compass_does_not_point_at_what_is_used_up_or_too_far()
        {
            int reach = Kit.Data.ConfigInt("obs_compass_radius");
            var sim = Kit.NewSim(2 * reach + 20, 9);
            var agent = sim.AddAgent(reach + 5, 4, Kit.Neutral);
            sim.World.PlaceNode(agent.X + 3, 4, Kit.Data.NodeById("berry_bush"));
            sim.World.NodeCharges[sim.World.Index(agent.X + 3, 4)] = 0;
            sim.World.PlaceNode(agent.X + reach + 1, 4, Kit.Data.NodeById("pine_tree"));
            sim.World.PlaceNode(agent.X - reach, 4, Kit.Data.NodeById("cotton_plant"));

            var (_, v) = See(sim, agent);

            int c = sim.Observation.Fields["compass"].Start;
            Assert.AreEqual(0f, v[c + 5], "the picked-over bush");
            Assert.AreEqual(0f, v[c + 8], "the tree one step out of reach");
            Assert.AreEqual(-1f, v[c + 12], "the cotton at the very edge is known: west");
            Assert.AreEqual(0f, v[c + 14], 1e-6f, "and as far as the compass goes");
        }

        [Test]
        public void The_compass_points_to_the_nearest_by_the_way_a_villager_walks()
        {
            var sim = Kit.NewSim(30, 20);
            var agent = sim.AddAgent(10, 10, Kit.Neutral);
            // A lake between the villager and the bush two tiles east; the way round is at the far north.
            for (int y = 0; y < 19; y++) sim.World.SetTerrain(11, y, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(12, 10, Kit.Data.NodeById("berry_bush"));
            sim.World.PlaceNode(10, 3, Kit.Data.NodeById("berry_bush"));

            var (_, v) = See(sim, agent);

            int c = sim.Observation.Fields["compass"].Start + 3;
            CollectionAssert.AreEqual(new[] { 0f, -1f }, v.Skip(c).Take(2).ToArray(), "the bush seven tiles south is six steps away; the one behind the lake is over twenty");
            Assert.IsTrue(sim.CompassTarget(agent.Index, Simulation.CompassFood, out int tile));
            Assert.AreEqual(sim.World.Index(10, 3), tile);
        }

        [Test]
        public void The_compass_points_only_to_what_the_villager_could_use()
        {
            var sim = Kit.NewSim(30, 9);
            var agent = sim.AddAgent(10, 4, Kit.Neutral);
            sim.World.PlaceNode(13, 4, Kit.Data.NodeById("surface_rock"));
            sim.World.PlaceNode(7, 4, Kit.Data.NodeById("oak_tree"));
            sim.World.PlaceNode(10, 7, Kit.Data.NodeById("berry_bush"));
            sim.World.SetTerrain(10, 1, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(10, 1, Kit.Data.NodeById("water_source"));
            int c = sim.Observation.Fields["compass"].Start;

            var (_, v) = See(sim, agent);
            Assert.AreEqual(0f, v[c + 11], "a rock is no use without a pickaxe");
            Assert.AreEqual(0f, v[c + 17], "nor an oak without a good axe");
            Assert.Greater(v[c + 5], 0f, "the bush is");

            Village.Give(sim, agent, "wooden_pickaxe", 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item("wooden_pickaxe")) });
            (_, v) = See(sim, agent);
            CollectionAssert.AreEqual(new[] { 1f, 0f }, v.Skip(c + 9).Take(2).ToArray(), "pickaxe in hand: the rock, east");

            Village.FillPack(sim, agent);
            (_, v) = See(sim, agent);
            Assert.AreEqual(0f, v[c + 5], "with no room in the pack the bush is no use");
            Assert.Greater(v[c + 11], 0f, "the rock still is: the stack of stone in the pack has room for more");
            CollectionAssert.AreEqual(new[] { 0f, -1f }, v.Skip(c).Take(2).ToArray(), "water still is: it can be drunk where it is");
        }

        [Test]
        public void The_compass_points_to_the_storage_the_fire_and_the_building_site()
        {
            var (sim, agent) = Village.One(width: 60, height: 40);
            sim.PlaceBuilding("basic_chest", 4, 20);
            sim.PlaceBuilding("campfire", 24, 4);
            var chest = Kit.Data.BuildingById["basic_chest"];
            Village.Give(sim, agent, chest.CostItem1, chest.CostQty1);
            Village.Do(sim, Village.Craft(sim, "basic_chest"));
            Assert.IsFalse(sim.BuildingAt(5, 4).Complete, "a site just begun, right in front");
            float reach = Kit.Data.ConfigInt("obs_compass_radius");

            var (_, v) = See(sim, agent);

            int c = sim.Observation.Fields["compass"].Start + 6 * 3;
            CollectionAssert.AreEqual(new[] { 0f, 1f, 1f - 16f / reach }, v.Skip(c).Take(3).ToArray(), "the chest, sixteen tiles north");
            CollectionAssert.AreEqual(new[] { 1f, 0f, 1f - 20f / reach }, v.Skip(c + 3).Take(3).ToArray(), "the fire, twenty tiles east");
            CollectionAssert.AreEqual(new[] { 1f, 0f, 1f - 1f / reach }, v.Skip(c + 6).Take(3).ToArray(), "the site in front");
        }

        // ------------------------------------------------------------------ the village

        [Test]
        public void A_villager_knows_what_the_village_has_built_and_learned()
        {
            var sim = Kit.NewSim(30, 30);
            var agent = sim.AddAgent(5, 5, Kit.Neutral);
            sim.PlaceBuilding("garden_plot", 20, 20);
            sim.PlaceBuilding("garden_plot", 24, 20);
            sim.Research("carpentry");
            var layout = sim.Observation;

            var (_, vector) = See(sim, agent);

            int works = layout.Fields["village works"].Start, known = layout.Fields["technologies known"].Start;
            Assert.AreEqual(0.5f, vector[works + Kit.Data.BuildingById["garden_plot"].Int - 1], 1e-6f, "two of four");
            Assert.AreEqual(0f, vector[works + Kit.Data.BuildingById["library"].Int - 1]);
            var tech = Kit.Data.Tech.ToList();
            Assert.AreEqual(1f, vector[known + tech.FindIndex(t => t.Id == "carpentry")]);
            Assert.AreEqual(1f, vector[known + tech.FindIndex(t => t.RpCost == 0)], "known from the start");
            Assert.AreEqual(0f, vector[known + tech.FindIndex(t => t.Id == "medicine")]);
            Assert.AreEqual(0f, vector[layout.Fields["technology studied"].Start]);
        }

        [Test]
        public void A_villager_knows_who_does_what_and_how_the_stores_stand()
        {
            var sim = Kit.NewSim(30, 30);
            var agent = sim.AddAgent(5, 5, Kit.Neutral);
            sim.SetRole(agent, "builder");
            sim.SetRole(sim.AddAgent(7, 5, Kit.Neutral), "miner");
            sim.SetRole(sim.AddAgent(9, 5, Kit.Neutral), "miner");
            var hungry = sim.AddAgent(11, 5, Kit.Neutral);
            hungry.Set(Need.Hunger, 0);
            var chest = sim.PlaceBuilding("basic_chest", 20, 20);
            chest.Storage[0] = new ItemStack { Item = Kit.Item("pine_wood"), Qty = 10 };
            chest.Storage[1] = new ItemStack { Item = Kit.Item("cobblestone"), Qty = 5 };
            var layout = sim.Observation;

            var (_, vector) = See(sim, agent);

            int roles = layout.Fields["role counts"].Start, stock = layout.Fields["village stock"].Start, state = layout.Fields["village state"].Start;
            Assert.AreEqual(2f / 4f, vector[roles + Kit.Data.RoleInt("miner") - 1], 1e-6f);
            Assert.AreEqual(1f / 4f, vector[roles + Kit.Data.RoleInt("builder") - 1], 1e-6f);
            Assert.AreEqual(0.10f, vector[stock + 1], 1e-6f, "wood");
            Assert.AreEqual(0.05f, vector[stock + 2], 1e-6f, "stone");
            Assert.AreEqual(4f / Kit.Data.ConfigInt("max_agents"), vector[state], 1e-6f);
            Assert.AreEqual(1f, vector[state + 1], "the Stone Age");
            Assert.AreEqual(0.25f, vector[state + 5], 1e-6f, "one of four is starving");
        }

        [Test]
        public void A_villager_sees_who_a_vote_is_about_and_whether_it_is_a_review()
        {
            var sim = Kit.NewSim(20, 9);
            sim.JobReviewsEnabled = true;
            var strong = Kit.Neutral;
            strong.Str = 9;
            var voter = sim.AddAgent(3, 4, Kit.Neutral);
            var subject = sim.AddAgent(6, 4, strong);
            sim.SetRole(voter, "cook");
            sim.SetRole(subject, "miner");
            var layout = sim.Observation;
            int context = layout.Fields["vote context"].Start;
            Assert.AreEqual(0f, See(sim, voter).vector[context], "no vote open");
            sim.CallJobReview();
            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Wait) });
            sim.Step(new[] { new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt("cook")), new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt("cook")) });
            Assert.AreEqual(subject.Index, sim.OpenVote.Subject);

            var (_, vector) = See(sim, voter);

            Assert.AreEqual(1f, vector[context], "a role vote");
            Assert.AreEqual(0f, vector[context + 1]);
            Assert.AreEqual(0.9f, vector[context + 3], 1e-6f, "the strength of the villager being voted on");
            Assert.AreEqual(Kit.Data.RoleInt("miner") / (float)Kit.Data.Roles.Max(r => r.Int), vector[layout.Fields["vote subject role"].Start], 1e-6f);
            Assert.AreEqual(1f, vector[layout.Fields["vote is review"].Start]);
        }

        [Test]
        public void A_villager_sees_the_health_of_whoever_it_faces_only_roughly_unless_it_is_a_doctor()
        {
            var sim = Kit.NewSim(20, 9);
            var watcher = sim.AddAgent(4, 4, Kit.Neutral);
            var hurt = sim.AddAgent(5, 4, Kit.Neutral);
            Kit.Face(watcher, 1, 0);
            hurt.Set(Need.Hp, 62);
            sim.SetRole(hurt, "farmer");
            var layout = sim.Observation;
            int faced = layout.Fields["faced agent"].Start;

            Assert.AreEqual(0.5f, See(sim, watcher).vector[faced], 1e-6f, "about half");
            Assert.AreEqual(Kit.Data.RoleInt("farmer") / (float)Kit.Data.Roles.Max(r => r.Int), See(sim, watcher).vector[faced + 3], 1e-6f);
            sim.SetRole(watcher, "doctor");
            Assert.AreEqual(0.62f, See(sim, watcher).vector[faced], 1e-6f, "a doctor sees it exactly");
            Kit.Face(watcher, -1, 0);
            Assert.AreEqual(0f, See(sim, watcher).vector[faced], "nobody there");
        }

        // ------------------------------------------------------------------ what can be done

        [Test]
        public void The_masks_say_exactly_what_the_engine_would_accept()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "berry", 2);
            Village.Give(sim, agent, "pine_wood", 5);
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            var layout = sim.Observation;
            var actions = new bool[layout.ActionCount];
            var table = new byte[layout.ActionCount * layout.ArgCount];

            sim.GetMasks(agent.Index, actions, table);

            for (int action = 0; action < layout.ActionCount; action++)
            {
                bool any = false;
                for (int arg = 0; arg < layout.ArgCount; arg++)
                {
                    bool valid = sim.IsValid(agent.Index, new AgentAction(action, arg));
                    bool shown = table[action * layout.ArgCount + arg] != 0;
                    // An action that takes no argument is offered once, with argument 0.
                    if (!sim.TakesArgument(action)) valid &= arg == 0;
                    Assert.AreEqual(valid, shown, Kit.Data.Actions.First(a => a.Id == action).Name + " " + arg);
                    any |= valid;
                }
                Assert.AreEqual(any, actions[action], Kit.Data.Actions.First(a => a.Id == action).Name);
            }
            Assert.IsTrue(actions[sim.Actions.Harvest]);
            Assert.IsTrue(table[sim.Actions.Consume * layout.ArgCount + Kit.Item("berry")] != 0);
            Assert.IsFalse(table[sim.Actions.CraftBuild * layout.ArgCount + Kit.Data.ArgFor("basic_chest")] != 0, "the wood is there, but a bush stands where the chest would go");
            Assert.IsTrue(table[sim.Actions.CraftBuild * layout.ArgCount + Kit.Data.ArgFor("wooden_spear")] != 0);
        }

        [Test]
        public void Looking_changes_nothing()
        {
            var (sim, agent) = Village.One();
            sim.AddAgent(7, 4, Kit.Neutral);
            ulong before = sim.ComputeStateHash();
            var layout = sim.Observation;

            See(sim, agent);
            sim.GetMasks(agent.Index, new bool[layout.ActionCount], new byte[layout.ActionCount * layout.ArgCount]);

            Assert.AreEqual(before, sim.ComputeStateHash());
        }
    }
}
