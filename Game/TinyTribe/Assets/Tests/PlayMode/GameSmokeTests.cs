using System.Collections;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using TinyTribe.View;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyTribe.Tests.PlayMode
{
    /// <summary>
    /// Runs the real game in Play mode: launch screen, starting, time control, selection, stopping.
    /// Any error or exception logged while these run fails the test.
    /// </summary>
    public class GameSmokeTests
    {
        static GameApp App => GameApp.Instance;

        static GameSetup SmallGame(BrainKind brains = BrainKind.Survival, float ticksPerSecond = 200f)
        {
            // Tests leave no run folders behind; the one that is about recording asks for it.
            var setup = new GameSetup { Brains = brains, TicksPerSecond = ticksPerSecond, Record = false };
            setup.Sim.Seed = 1;
            setup.Sim.Agents = 6;
            setup.Sim.MapWidth = 64;
            setup.Sim.MapHeight = 64;
            return setup;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>A tile near the village centre with nothing on it. skip picks the next one along.</summary>
        static (int x, int y) FreeGround(Simulation sim, int skip = 0)
        {
            var world = sim.World;
            for (int r = 2; r < 24; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = world.SpawnX + dx, y = world.SpawnY + dy;
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r || !sim.CanEnter(x, y) || world.NodeAt(x, y) != 0) continue;
                        if (skip-- == 0) return (x, y);
                    }
            Assert.Fail("no free ground near the village");
            return default;
        }

        /// <summary>The lower-left tile of a square of free ground, a little way from the village centre.</summary>
        static (int x, int y) FreeSquare(Simulation sim, int size)
        {
            var world = sim.World;
            for (int r = 4; r < 24; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = world.SpawnX + dx, y = world.SpawnY + dy;
                        bool free = true;
                        for (int j = 0; j < size && free; j++)
                            for (int i = 0; i < size && free; i++)
                                free = sim.CanEnter(x + i, y + j) && world.NodeAt(x + i, y + j) == 0;
                        if (free) return (x, y);
                    }
            Assert.Fail("no free square of " + size + " tiles near the village");
            return default;
        }

        /// <summary>Turns the villager to a neighbouring tile with nothing on it and returns that tile.</summary>
        static (int x, int y) FaceFreeGround(Simulation sim, Agent agent)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = agent.X + dx, y = agent.Y + dy;
                if (!sim.CanEnter(x, y) || sim.World.NodeAt(x, y) != 0) continue;
                agent.FaceDx = dx;
                agent.FaceDy = dy;
                return (x, y);
            }
            Assert.Fail("the villager is boxed in");
            return default;
        }

        [UnitySetUp]
        public IEnumerator BackToTheLaunchScreen()
        {
            yield return null;
            Assert.IsNotNull(App, "the game object should create itself when Play starts");
            if (App.IsRunning) App.StopGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Game_boots_to_the_launch_screen_with_its_data_loaded()
        {
            yield return Frames(2);

            Assert.IsFalse(App.IsRunning);
            Assert.IsNotNull(App.Data);
            Assert.IsNotNull(App.Hud);
        }

        [UnityTest]
        public IEnumerator Launch_screen_draws()
        {
            yield return Frames(5);

            Assert.Greater(App.Hud.FramesDrawn, 0);
        }

        [UnityTest]
        public IEnumerator Starting_a_game_draws_the_map_and_one_sprite_per_villager()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            yield return Frames(3);

            Assert.IsTrue(App.IsRunning);
            var sim = App.Runner.Sim;
            Assert.IsTrue(App.World.HasTerrainTile(0, 0));
            Assert.IsTrue(App.World.HasTerrainTile(sim.World.Width - 1, sim.World.Height - 1));
            Assert.AreEqual(sim.Agents.Count, App.Villagers.Count);
            foreach (var agent in sim.Agents)
            {
                var position = App.Villagers.PositionOf(agent.Index);
                Assert.AreEqual(agent.X + 0.5f, position.x, 0.01f);
                Assert.AreEqual(agent.Y + 0.5f, position.y, 0.01f);
            }
        }

        [UnityTest]
        public IEnumerator Map_shows_every_active_node_and_no_others()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            yield return Frames(2);
            var world = App.Runner.Sim.World;
            int waterSource = App.Data.NodeById("water_source").Int;
            int checkedNodes = 0;

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    // Water has no sprite of its own: the terrain already shows it.
                    bool expected = world.HasActiveNode(x, y) && world.NodeAt(x, y) != waterSource;
                    Assert.AreEqual(expected, App.World.IsNodeDrawn(x, y), "node at " + x + "," + y);
                    if (expected) checkedNodes++;
                }
            Assert.Greater(checkedNodes, 20);
        }

        [UnityTest]
        public IEnumerator Map_follows_nodes_being_used_up_and_growing_back()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            yield return Frames(2);
            var world = App.Runner.Sim.World;
            var bush = App.Data.NodeById("berry_bush");
            int tile = System.Array.IndexOf(world.Node, (short)bush.Int);
            Assert.GreaterOrEqual(tile, 0, "the test map should have a berry bush");
            int x = tile % world.Width, y = tile / world.Width;
            Assert.IsTrue(App.World.IsNodeDrawn(x, y));

            world.NodeCharges[tile] = 0;
            App.World.Apply(new[] { new SimEvent(SimEventType.NodeDepleted, -1, bush.Int, 0, x, y) });
            Assert.IsFalse(App.World.IsNodeDrawn(x, y), "a used-up bush is not drawn as harvestable");

            world.NodeCharges[tile] = (short)bush.Charges;
            App.World.Apply(new[] { new SimEvent(SimEventType.NodeRegrown, -1, bush.Int, 0, x, y) });
            Assert.IsTrue(App.World.IsNodeDrawn(x, y));
        }

        [UnityTest]
        public IEnumerator Every_terrain_and_resource_node_has_a_picture()
        {
            yield return null;
            var art = App.Art;

            foreach (var terrain in App.Data.Terrain)
                for (int variant = 0; variant < art.TerrainVariants; variant++)
                    Assert.IsNotNull(art.Terrain(terrain, variant), "terrain " + terrain.Id + " variant " + variant);
            foreach (var node in App.Data.ResourceNodes)
            {
                if (node.Id == "water_source" || node.Id == "fishing_spot") continue;
                Assert.IsNotNull(art.Node(node), "node " + node.Id);
            }
        }

        [UnityTest]
        public IEnumerator Villagers_have_a_picture_for_every_direction_and_walk_frame()
        {
            yield return null;
            var art = App.Art;
            int max = App.Data.ConfigInt("max_agents");
            var directions = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };

            for (int villager = 0; villager < max; villager++)
                foreach (var (dx, dy) in directions)
                    for (int frame = 0; frame <= 8; frame++)
                        Assert.IsNotNull(art.Villager(villager, dx, dy, frame, out _), "villager " + villager + " facing " + dx + "," + dy + " frame " + frame);
        }

        [UnityTest]
        public IEnumerator Every_building_flame_and_animal_has_a_picture()
        {
            yield return null;
            var art = App.Art;
            var directions = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };

            foreach (var building in App.Data.Buildings)
            {
                var picture = art.Building(building);
                Assert.IsNotNull(picture, "building " + building.Id);
                Assert.GreaterOrEqual(picture.bounds.size.x, building.FootprintW - 0.01f, building.Id + " should cover its footprint");
            }
            Assert.Greater(art.FlameFrames, 0);
            for (int frame = 0; frame < art.FlameFrames * 2; frame++) Assert.IsNotNull(art.Flame(frame), "flame " + frame);
            foreach (var animal in App.Data.Animals)
                foreach (var (dx, dy) in directions)
                    for (int frame = 0; frame <= 6; frame++)
                        Assert.IsNotNull(art.Animal(animal, dx, dy, frame, out _), animal.Id + " facing " + dx + "," + dy + " frame " + frame);
        }

        [UnityTest]
        public IEnumerator Buildings_are_drawn_where_they_stand_and_a_site_is_fainter_than_a_finished_building()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            App.Runner.Paused = true;
            var sim = App.Runner.Sim;
            var (chestX, chestY) = FreeGround(sim);
            var chest = sim.PlaceBuilding("basic_chest", chestX, chestY);
            var (siteX, siteY) = FreeGround(sim, 3);
            var site = sim.PlaceBuilding("stone_well", siteX, siteY, complete: false);
            yield return Frames(2);

            Assert.AreEqual(sim.Buildings.Count, App.Buildings.Count);
            Assert.IsTrue(App.Buildings.IsDrawn(chest.Index));
            Assert.IsTrue(App.Buildings.IsDrawn(site.Index));
            Assert.AreEqual(new Vector3(chestX, chestY, 0f), App.Buildings.PositionOf(chest.Index));
            Assert.AreEqual(1f, App.Buildings.Opacity(chest.Index), 0.001f);
            float early = App.Buildings.Opacity(site.Index);
            Assert.Less(early, 0.6f);

            site.Progress = site.Def.BuildTicks * 0.9f;
            yield return Frames(2);
            Assert.Greater(App.Buildings.Opacity(site.Index), early, "a site fills in as the work gets on");
            Assert.Less(App.Buildings.Opacity(site.Index), 1f);
        }

        [UnityTest]
        public IEnumerator A_lit_campfire_shows_a_flame_and_a_burnt_out_one_does_not()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            App.Runner.Paused = true;
            var sim = App.Runner.Sim;
            var (x, y) = FreeGround(sim);
            var fire = sim.PlaceBuilding("campfire", x, y);
            yield return Frames(2);
            Assert.IsTrue(fire.IsLit);
            Assert.IsTrue(App.Buildings.ShowsFlame(fire.Index));

            fire.FuelTicks = 0;
            yield return Frames(2);
            Assert.IsFalse(App.Buildings.ShowsFlame(fire.Index));
            Assert.IsTrue(App.Buildings.IsDrawn(fire.Index), "the fireplace itself stays");
        }

        [UnityTest]
        public IEnumerator Animals_are_drawn_on_their_tiles_and_disappear_when_they_die()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            App.Runner.Paused = true;
            var sim = App.Runner.Sim;
            var (x, y) = FreeGround(sim);
            var wolf = sim.SpawnAnimal("wolf", x, y);
            yield return Frames(2);

            Assert.AreEqual(sim.Animals.Count, App.Animals.Count);
            int alive = 0;
            foreach (var animal in sim.Animals)
            {
                Assert.AreEqual(animal.Alive, App.Animals.IsDrawn(animal.Index), animal.Def.Id + " " + animal.Index);
                if (!animal.Alive) continue;
                alive++;
                var position = App.Animals.PositionOf(animal.Index);
                Assert.AreEqual(animal.X + 0.5f, position.x, 0.01f);
                Assert.AreEqual(animal.Y + 0.5f, position.y, 0.01f);
            }
            Assert.Greater(alive, 5, "the map should start with animals on it");

            sim.DespawnAnimal(wolf);
            yield return Frames(2);
            Assert.IsFalse(App.Animals.IsDrawn(wolf.Index));
        }

        [UnityTest]
        public IEnumerator Something_put_down_shows_on_the_map_until_it_is_picked_up()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 1;
            App.StartGame(setup);
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            int bone = App.Data.ItemById["bone"].Int;
            sim.AddItem(agent, bone, 2);
            var (x, y) = FaceFreeGround(sim, agent);
            App.Select(0);
            App.SetControl(true);
            Assert.IsFalse(App.World.IsNodeDrawn(x, y));

            // Stepping while paused, as the Step button does.
            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Deposit, bone));
            App.StepOnce();
            Assert.AreEqual(0, agent.Count(bone));
            Assert.IsTrue(App.World.IsNodeDrawn(x, y), "the pile should be on the map");
            yield return Frames(2);
            Assert.IsTrue(App.World.IsNodeDrawn(x, y), "and still be there on the next frames");

            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Harvest));
            App.StepOnce();
            Assert.AreEqual(2, agent.Count(bone));
            Assert.IsFalse(App.World.IsNodeDrawn(x, y));
        }

        [UnityTest]
        public IEnumerator The_players_villager_can_be_told_to_build_and_keeps_at_it()
        {
            var setup = SmallGame(BrainKind.Idle, 100f);
            setup.Sim.Agents = 1;
            App.StartGame(setup);
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            var chestDef = App.Data.BuildingById["basic_chest"];
            sim.AddItem(agent, App.Data.ItemById["pine_wood"].Int, chestDef.CostQty1);
            var (x, y) = FaceFreeGround(sim, agent);
            App.Select(0);
            App.SetControl(true);

            App.Runner.Manual.Hold(new AgentAction(sim.Actions.CraftBuild, App.Data.ArgFor("basic_chest")));
            App.Runner.Paused = false;
            float waited = 0f;
            while ((sim.BuildingAt(x, y) == null || !sim.BuildingAt(x, y).Complete) && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
            App.Runner.Paused = true;
            yield return Frames(2);

            var chest = sim.BuildingAt(x, y);
            Assert.IsNotNull(chest, "a chest should stand in front of the villager");
            Assert.IsTrue(chest.Complete);
            Assert.IsTrue(App.Buildings.IsDrawn(chest.Index));
            Assert.AreEqual(1f, App.Buildings.Opacity(chest.Index), 0.001f);
        }

        [UnityTest]
        public IEnumerator Tall_nodes_in_front_are_drawn_over_villagers_behind_them()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            yield return Frames(2);
            int height = App.Runner.Sim.World.Height;

            // A villager one row up the screen from a tree stands behind it; one on the same row stands in front.
            Assert.Less(WorldView.RowOrder(height, 11) + 1, WorldView.RowOrder(height, 10));
            Assert.Greater(WorldView.RowOrder(height, 10) + 1, WorldView.RowOrder(height, 10));
        }

        [UnityTest]
        public IEnumerator The_scene_darkens_at_night_and_brightens_again_by_day()
        {
            var setup = SmallGame(BrainKind.Idle, 400f);
            App.StartGame(setup);
            var sim = App.Runner.Sim;
            Assert.AreEqual(0f, App.Darkness);

            float waited = 0f;
            while (!sim.IsNight && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(sim.IsNight, "night should arrive within ten seconds at this speed");
            App.Runner.Paused = true;
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.Greater(App.Darkness, 0.9f);

            App.Runner.Paused = false;
            waited = 0f;
            while (sim.IsNight && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
            App.Runner.Paused = true;
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.Less(App.Darkness, 0.1f);
        }

        [UnityTest]
        public IEnumerator Game_keeps_running_when_its_window_is_not_in_front()
        {
            yield return null;

            Assert.IsTrue(Application.runInBackground);
        }

        [UnityTest]
        public IEnumerator Village_totals_are_shown_from_the_first_frame_of_a_game()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            yield return Frames(2);

            Assert.AreEqual(App.Runner.Sim.Agents.Count, App.Hud.AliveShown);
        }

        [UnityTest]
        public IEnumerator A_tall_tree_turns_see_through_while_a_villager_stands_behind_it()
        {
            // One villager alone in the village clearing, so the tiles around it are free.
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 1;
            App.StartGame(setup);
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            int treeX = agent.X, treeY = agent.Y - 1;
            sim.World.PlaceNode(treeX, treeY, App.Data.NodeById("pine_tree"));
            App.World.RefreshNode(treeX, treeY);
            Assume.That(App.World.IsTallNode(treeX, treeY), "the installed art draws trees one tile tall, so nothing can hide behind them");

            yield return Frames(2);
            Assert.Less(App.World.NodeOpacity(treeX, treeY), 0.8f, "the villager right behind the tree should show through it");

            // Walk the villager two tiles up the screen, clear of the tree.
            App.Select(agent.Index);
            App.SetControl(true);
            for (int step = 0; step < 2; step++)
            {
                App.Runner.Manual.Queue(new AgentAction(sim.Actions.MoveNorth));
                App.Runner.StepOnce();
            }
            Assert.AreEqual(treeY + 3, agent.Y, "the villager should have walked away");
            yield return Frames(2);

            Assert.AreEqual(1f, App.World.NodeOpacity(treeX, treeY), 0.001f, "the tree is solid again once nobody is behind it");
        }

        [UnityTest]
        public IEnumerator Time_runs_while_playing_and_stops_when_paused()
        {
            App.StartGame(SmallGame());
            yield return new WaitForSecondsRealtime(0.4f);
            long running = App.Runner.Sim.Tick;
            Assert.Greater(running, 0);

            App.Runner.Paused = true;
            yield return null;
            long paused = App.Runner.Sim.Tick;
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.AreEqual(paused, App.Runner.Sim.Tick);
        }

        [UnityTest]
        public IEnumerator Villager_sprites_follow_their_villagers()
        {
            App.StartGame(SmallGame(BrainKind.Random, 60f));
            var sim = App.Runner.Sim;
            var start = new (int, int)[sim.Agents.Count];
            for (int i = 0; i < start.Length; i++) start[i] = (sim.Agents[i].X, sim.Agents[i].Y);
            yield return new WaitForSecondsRealtime(1.0f);
            App.Runner.Paused = true;
            yield return new WaitForSecondsRealtime(0.6f);

            bool anyMoved = false;
            foreach (var agent in sim.Agents)
            {
                anyMoved |= (agent.X, agent.Y) != start[agent.Index];
                var position = App.Villagers.PositionOf(agent.Index);
                Assert.AreEqual(agent.X + 0.5f, position.x, 0.05f, "villager " + agent.Index + " x");
                Assert.AreEqual(agent.Y + 0.5f, position.y, 0.05f, "villager " + agent.Index + " y");
            }
            Assert.IsTrue(anyMoved, "random villagers should have wandered");
        }

        [UnityTest]
        public IEnumerator Selecting_a_villager_and_taking_control_of_it()
        {
            App.StartGame(SmallGame());
            yield return null;

            App.Select(2);
            Assert.AreEqual(2, App.SelectedAgent);
            Assert.AreEqual(-1, App.Runner.ControlledAgent, "selecting only spectates");

            App.SetControl(true);
            Assert.AreEqual(2, App.Runner.ControlledAgent);

            App.Select(3);
            Assert.AreEqual(3, App.SelectedAgent);
            Assert.AreEqual(-1, App.Runner.ControlledAgent, "control is released when another villager is selected");
        }

        [UnityTest]
        public IEnumerator Selecting_an_index_that_does_not_exist_clears_the_selection()
        {
            App.StartGame(SmallGame());
            yield return null;
            App.Select(1);

            App.Select(99);

            Assert.AreEqual(-1, App.SelectedAgent);
        }

        [UnityTest]
        public IEnumerator All_windows_draw_during_a_game()
        {
            App.StartGame(SmallGame());
            App.Select(0);
            App.SetControl(true);
            int before = App.Hud.FramesDrawn;

            yield return Frames(10);

            Assert.Greater(App.Hud.FramesDrawn, before);
        }

        [UnityTest]
        public IEnumerator Windows_draw_for_a_villager_in_front_of_a_chest_with_things_to_make()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 1;
            App.StartGame(setup);
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            var (x, y) = FaceFreeGround(sim, agent);
            var chest = sim.PlaceBuilding("basic_chest", x, y);
            chest.Storage[0] = new ItemStack { Item = App.Data.ItemById["berry"].Int, Qty = 6 };
            chest.Storage[4] = new ItemStack { Item = App.Data.ItemById["berry"].Int, Qty = 2 };
            sim.AddItem(agent, App.Data.ItemById["pine_wood"].Int, 6);
            sim.AddItem(agent, App.Data.ItemById["raw_meat"].Int, 1);
            var (fireX, fireY) = FreeGround(sim, 5);
            sim.PlaceBuilding("campfire", fireX, fireY);
            var (tentX, tentY) = FreeSquare(sim, 2);
            sim.PlaceBuilding("hide_tent", tentX, tentY, complete: false);
            App.Select(0);
            App.SetControl(true);
            int before = App.Hud.FramesDrawn;

            yield return Frames(10);

            Assert.Greater(App.Hud.FramesDrawn, before);
            Assert.IsTrue(sim.IsValid(0, new AgentAction(sim.Actions.Take, App.Data.ItemById["berry"].Int)), "the Take button has something to do");
        }

        [UnityTest]
        public IEnumerator Windows_draw_while_the_council_votes_and_the_player_can_vote()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 2;
            App.StartGame(setup);
            App.Runner.Paused = true;
            var sim = App.Runner.Sim;
            App.StepOnce();
            Assert.IsNotNull(sim.OpenVote, "a vote on the first villager's job opens on the first tick");
            App.Select(0);
            App.SetControl(true);
            int before = App.Hud.FramesDrawn;

            yield return Frames(6);
            Assert.Greater(App.Hud.FramesDrawn, before);

            int builder = App.Data.RoleInt("builder");
            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Vote, builder));
            App.StepOnce();
            Assert.IsTrue(sim.OpenVote.HasVoted(0));
            yield return Frames(6);

            // Nobody else votes: the player's vote decides when the time is up.
            for (int t = 0; t < App.Data.ConfigInt("vote_duration_ticks") && sim.Agents[0].Role == 0; t++) App.StepOnce();
            Assert.AreEqual(builder, sim.Agents[0].Role);
            yield return Frames(4);
        }

        [UnityTest]
        public IEnumerator A_finished_farm_shows_its_farmland_on_the_map()
        {
            App.StartGame(SmallGame(BrainKind.Idle));
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            sim.Research("agriculture");
            var (x, y) = FreeSquare(sim, 6);
            int farmland = App.Data.TerrainById("farmland").Int;
            Assert.AreNotEqual(farmland, App.World.TerrainDrawn(x + 1, y));

            var farm = sim.PlaceBuilding("farm_windmill", x + 1, y + 1);
            // Placed from outside a tick, so the map is told by hand; in play the events of the tick do it.
            App.World.Apply(sim.Events);
            yield return Frames(2);

            Assert.AreEqual(farmland, sim.World.TerrainAt(x + 1, y));
            Assert.AreEqual(farmland, App.World.TerrainDrawn(x + 1, y));
            Assert.IsTrue(App.Buildings.IsDrawn(farm.Index));
        }

        [UnityTest]
        public IEnumerator The_player_can_vote_on_research_and_study_at_the_library()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 1;
            // A new building has the Council go over the jobs first; this is about the vote that follows.
            setup.Sim.JobReviews = false;
            App.StartGame(setup);
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            sim.SetRole(agent, "scholar");
            // A library right in front of the villager: its near corner on a free tile, the rest wherever it fits.
            Building library = null;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int cornerX = dx < 0 ? agent.X - 3 : agent.X + dx, cornerY = dy < 0 ? agent.Y - 3 : agent.Y + dy;
                try { library = sim.PlaceBuilding("library", cornerX, cornerY); }
                catch (System.InvalidOperationException) { continue; }
                agent.FaceDx = dx;
                agent.FaceDy = dy;
                break;
            }
            Assume.That(library != null, "no room for a library next to the villager on this map");
            App.Select(0);
            App.SetControl(true);

            App.StepOnce();
            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            yield return Frames(4);
            int agriculture = 0;
            for (int i = 0; i < App.Data.Tech.Count; i++)
                if (App.Data.Tech[i].Id == "agriculture") agriculture = i;
            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Vote, agriculture));
            App.StepOnce();
            Assert.AreEqual(App.Data.Tech[agriculture].Id, sim.ResearchingTech.Id);

            int study = -1;
            foreach (var arg in App.Data.ActionArgs)
                if (arg.Kind == "research") study = arg.Int;
            App.Runner.Manual.Hold(new AgentAction(sim.Actions.CraftBuild, study));
            for (int t = 0; t < 5; t++) App.StepOnce();
            yield return Frames(4);

            Assert.Greater(sim.ResearchPoints, 0f);
        }

        [UnityTest]
        public IEnumerator An_elders_circle_is_drawn_and_carpentry_can_be_studied_at_it()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 1;
            setup.Sim.JobReviews = false;
            App.StartGame(setup);
            App.Runner.Paused = true;
            yield return Frames(2);
            var sim = App.Runner.Sim;
            var agent = sim.Agents[0];
            sim.SetRole(agent, "scholar");
            Building circle = null;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int cornerX = dx < 0 ? agent.X - 2 : agent.X + dx, cornerY = dy < 0 ? agent.Y - 2 : agent.Y + dy;
                try { circle = sim.PlaceBuilding("study_circle", cornerX, cornerY); }
                catch (System.InvalidOperationException) { continue; }
                agent.FaceDx = dx;
                agent.FaceDy = dy;
                break;
            }
            Assume.That(circle != null, "no room for a circle next to the villager on this map");
            App.Select(0);
            App.SetControl(true);

            App.StepOnce();
            yield return Frames(4);
            Assert.IsTrue(App.Buildings.IsDrawn(circle.Index));
            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            int carpentry = 0, medicine = 0;
            for (int i = 0; i < App.Data.Tech.Count; i++)
            {
                if (App.Data.Tech[i].Id == "carpentry") carpentry = i;
                if (App.Data.Tech[i].Id == "medicine") medicine = i;
            }
            Assert.IsFalse(sim.IsValid(0, new AgentAction(sim.Actions.Vote, medicine)), "medicine takes a library");
            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Vote, carpentry));
            App.StepOnce();
            Assert.AreEqual("carpentry", sim.ResearchingTech.Id);

            int study = -1;
            foreach (var arg in App.Data.ActionArgs)
                if (arg.Kind == "research") study = arg.Int;
            App.Runner.Manual.Hold(new AgentAction(sim.Actions.CraftBuild, study));
            for (int t = 0; t < 5; t++) App.StepOnce();
            yield return Frames(4);

            Assert.Greater(sim.ResearchPoints, 0f);
        }

        [UnityTest]
        public IEnumerator Windows_draw_through_a_job_review_and_the_player_can_change_a_job()
        {
            var setup = SmallGame(BrainKind.Idle);
            setup.Sim.Agents = 2;
            App.StartGame(setup);
            App.Runner.Paused = true;
            var sim = App.Runner.Sim;
            Assert.IsTrue(sim.JobReviewsEnabled, "a new game reviews its jobs");
            sim.SetRole(sim.Agents[0], "cook");
            sim.SetRole(sim.Agents[1], "miner");
            sim.CallJobReview();
            App.Select(0);
            App.SetControl(true);

            App.StepOnce();
            Assert.IsTrue(sim.OpenVote != null && sim.OpenVote.Review && sim.OpenVote.Subject == 0);
            int before = App.Hud.FramesDrawn;
            yield return Frames(6);
            Assert.Greater(App.Hud.FramesDrawn, before);

            int builder = App.Data.RoleInt("builder");
            App.Runner.Manual.Queue(new AgentAction(sim.Actions.Vote, builder));
            for (int t = 0; t <= App.Data.ConfigInt("vote_duration_ticks") && sim.Agents[0].Role != builder; t++) App.StepOnce();
            yield return Frames(4);

            Assert.AreEqual(builder, sim.Agents[0].Role, "the cook is the builder now");
            Assert.AreEqual(1, sim.OpenVote.Subject, "and the review moves on to the next villager");
        }

        [UnityTest]
        public IEnumerator The_achievements_and_tree_windows_draw_and_follow_the_game()
        {
            App.StartGame(SmallGame());
            App.Runner.Paused = true;
            for (int t = 0; t < 40; t++) App.StepOnce();
            App.Hud.ShowAchievements = true;
            App.Hud.ShowTree = true;
            int before = App.Hud.FramesDrawn;

            yield return Frames(8);

            Assert.Greater(App.Hud.FramesDrawn, before);
            Assert.IsNull(App.Recorder, "this game was asked not to be recorded");
            Assert.IsNotNull(App.Stats);
            Assert.AreEqual(40, App.Stats.Get("ticks"));
            Assert.IsTrue(App.Stats.HasAchieved("jobs_for_all"), "the bots vote everyone a job on the first morning");
            App.Hud.ShowAchievements = false;
            App.Hud.ShowTree = false;
        }

        [UnityTest]
        public IEnumerator A_game_is_recorded_until_it_is_stopped()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tinytribe-tests", System.Guid.NewGuid().ToString("N"));
            string usual = App.RunsFolder;
            App.RunsFolder = folder;
            try
            {
                var setup = SmallGame();
                setup.Record = true;
                App.StartGame(setup);
                App.Runner.Paused = true;
                for (int t = 0; t < 30; t++) App.StepOnce();
                yield return Frames(2);
                var recorder = App.Recorder;
                Assert.IsNotNull(recorder);
                Assert.IsFalse(recorder.Finished);
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(recorder.Folder, "run.json")));

                App.StopGame();

                Assert.IsTrue(recorder.Finished);
                var run = RunReplay.Read(recorder.Folder);
                Assert.AreEqual(30, run.Result.Ticks);
                Assert.AreEqual("stopped", run.Result.EndReason);
                Assert.AreEqual(Application.unityVersion, run.Host["unity"]);
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(folder, "index.csv")));
            }
            finally
            {
                App.RunsFolder = usual;
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
        }

        [UnityTest]
        public IEnumerator A_game_can_be_played_by_a_trained_network()
        {
            var installed = TinyTribe.Neural.NeuralBrain.Installed();
            if (installed.Length == 0) Assert.Ignore("No trained network is installed. Train one and run ML/install_model.py.");
            var setup = SmallGame(BrainKind.Neural);
            setup.Model = installed[0];
            App.StartGame(setup);
            App.Runner.Paused = true;
            Assert.IsNotNull(App.Neural);
            Assert.AreEqual(installed[0], App.Neural.Info.Name);
            var sim = App.Runner.Sim;
            var start = sim.Agents.Select(a => (a.X, a.Y)).ToArray();

            for (int t = 0; t < 60; t++) App.StepOnce();
            yield return Frames(4);

            Assert.AreEqual(60, sim.Tick);
            Assert.IsTrue(sim.Agents.Where((a, i) => (a.X, a.Y) != start[i]).Any(), "its villagers do something");
            Assert.AreEqual(60, App.Stats.Get("ticks"));

            App.StopGame();
            Assert.IsNull(App.Neural, "the network is let go with the game");
        }

        [UnityTest]
        public IEnumerator Stopping_returns_to_the_launch_screen_and_removes_the_map()
        {
            App.StartGame(SmallGame());
            yield return Frames(2);

            App.StopGame();
            yield return Frames(2);

            Assert.IsFalse(App.IsRunning);
            Assert.AreEqual(-1, App.SelectedAgent);
            Assert.IsNull(Object.FindFirstObjectByType<WorldView>());
            Assert.IsNull(Object.FindFirstObjectByType<AgentLayer>());
            Assert.IsNull(Object.FindFirstObjectByType<BuildingLayer>());
            Assert.IsNull(Object.FindFirstObjectByType<AnimalLayer>());
        }

        [UnityTest]
        public IEnumerator A_second_game_can_be_started_after_the_first()
        {
            App.StartGame(SmallGame());
            yield return Frames(2);
            App.StopGame();
            yield return null;

            var setup = SmallGame();
            setup.Sim.Agents = 3;
            App.StartGame(setup);
            yield return Frames(2);

            Assert.AreEqual(3, App.Runner.Sim.Agents.Count);
            Assert.AreEqual(3, App.Villagers.Count);
        }
    }
}
