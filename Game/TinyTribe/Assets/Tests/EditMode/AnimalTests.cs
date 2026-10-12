using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    public class AnimalTests
    {
        static AnimalDef Def(string id) => Kit.Data.AnimalById(id);

        static Simulation Field() => Kit.NewSim(30, 9);

        static AgentAction[] Waiting(Simulation sim) => sim.Agents.Select(_ => new AgentAction(sim.Actions.Wait)).ToArray();

        static void Tick(Simulation sim, int times = 1)
        {
            for (int i = 0; i < times; i++) sim.Step(Waiting(sim));
        }

        static Agent Armed(Simulation sim, int x, int y, string weapon)
        {
            var agent = sim.AddAgent(x, y, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            if (weapon != null)
            {
                sim.AddItem(agent, Kit.Item(weapon), 1);
                sim.Step(sim.Agents.Select(a => a == agent ? new AgentAction(sim.Actions.Equip, Kit.Item(weapon)) : new AgentAction(sim.Actions.Wait)).ToArray());
            }
            return agent;
        }

        [Test]
        public void Animal_data_is_loaded()
        {
            CollectionAssert.AreEquivalent(new[] { "rabbit", "deer", "wolf", "bear" }, Kit.Data.Animals.Select(a => a.Id).ToArray());
            Assert.Greater(Def("wolf").AttackDamage, 0f);
        }

        [Test]
        public void A_spawned_animal_stands_on_its_tile_and_blocks_it()
        {
            var sim = Field();

            var bear = sim.SpawnAnimal("bear", 10, 4);

            Assert.AreSame(bear, sim.AnimalAt(10, 4));
            Assert.AreEqual(Def("bear").Hp, bear.Hp);
            Assert.IsFalse(sim.CanEnter(10, 4));
            Assert.Throws<InvalidOperationException>(() => sim.SpawnAnimal("rabbit", 10, 4));
        }

        [Test]
        public void A_rabbit_runs_from_a_villager_inside_its_flee_radius_at_its_own_speed()
        {
            var sim = Field();
            sim.AddAgent(2, 4, Kit.Neutral);
            var rabbit = sim.SpawnAnimal("rabbit", 4, 4);
            Assert.AreEqual(1.5f, Def("rabbit").SpeedTilesPerTick, "the expected steps below assume this speed");
            Assert.GreaterOrEqual(Def("rabbit").FleeRadius, 3);

            Tick(sim);
            Assert.AreEqual((5, 4), (rabbit.X, rabbit.Y), "one step on the first tick");

            Tick(sim);
            Assert.AreEqual((7, 4), (rabbit.X, rabbit.Y), "two steps on the second, making three in two ticks");
            Assert.IsNull(sim.AnimalAt(4, 4));
            Assert.AreSame(rabbit, sim.AnimalAt(7, 4));
        }

        [Test]
        public void A_fleeing_animal_goes_around_what_is_in_its_way()
        {
            var sim = Field();
            sim.AddAgent(2, 4, Kit.Neutral);
            var rabbit = sim.SpawnAnimal("rabbit", 4, 4);
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));

            Tick(sim);

            Assert.AreNotEqual((4, 4), (rabbit.X, rabbit.Y), "it should still get away");
            Assert.IsTrue(sim.World.HasActiveNode(5, 4));
        }

        [Test]
        public void A_slow_animal_moves_every_other_tick()
        {
            var sim = Field();
            sim.AddAgent(2, 4, Kit.Neutral);
            var bear = sim.SpawnAnimal("bear", 5, 4);
            Assert.AreEqual(0.5f, Def("bear").SpeedTilesPerTick);
            Assert.GreaterOrEqual(Def("bear").AggroRadius, 3);

            Tick(sim);
            Assert.AreEqual(5, bear.X);
            Tick(sim);
            Assert.AreEqual(4, bear.X, "one step toward the villager after two ticks");
        }

        [Test]
        public void A_wolf_walks_up_to_a_villager_and_bites()
        {
            var sim = Field();
            var agent = sim.AddAgent(2, 4, Kit.Neutral);
            var wolf = sim.SpawnAnimal("wolf", 5, 4);
            Assert.GreaterOrEqual(Def("wolf").AggroRadius, 3);

            Tick(sim, 2);
            Assert.AreEqual((3, 4), (wolf.X, wolf.Y), "next to the villager after two steps");
            Assert.AreEqual(100f, agent.Get(Need.Hp), "not bitten while it was still walking");

            Tick(sim);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.AgentHurt && e.Agent == agent.Index));
            Assert.AreEqual(100 - Def("wolf").AttackDamage, agent.Get(Need.Hp), 0.2f);
        }

        [Test]
        public void A_wolf_waits_for_its_cooldown_between_bites()
        {
            var sim = Field();
            var agent = sim.AddAgent(2, 4, Kit.Neutral);
            sim.SpawnAnimal("wolf", 3, 4);
            int cooldown = Def("wolf").AttackCooldownTicks;
            Assert.Greater(cooldown, 1);
            int bites = 0;

            for (int t = 0; t < cooldown; t++)
            {
                Tick(sim);
                bites += sim.Events.Count(e => e.Type == SimEventType.AgentHurt);
            }
            Assert.AreEqual(1, bites, "one bite in the first " + cooldown + " ticks");

            Tick(sim);
            bites += sim.Events.Count(e => e.Type == SimEventType.AgentHurt);
            Assert.AreEqual(2, bites);
            Assert.Less(agent.Get(Need.Hp), 100 - Def("wolf").AttackDamage);
        }

        [Test]
        public void An_animal_ignores_a_villager_outside_its_radius()
        {
            var sim = Field();
            var agent = sim.AddAgent(2, 4, Kit.Neutral);
            sim.SpawnAnimal("wolf", 2 + Def("wolf").AggroRadius + 6, 4);

            Tick(sim, 6);

            Assert.AreEqual(100f, agent.Get(Need.Hp));
            Assert.IsFalse(sim.Animals.Any(a => Math.Max(Math.Abs(a.X - agent.X), Math.Abs(a.Y - agent.Y)) <= 1), "it only wandered");
        }

        [Test]
        public void A_bite_can_kill()
        {
            var sim = Field();
            var agent = sim.AddAgent(2, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("berry"), 3);
            sim.SpawnAnimal("wolf", 3, 4);
            agent.Set(Need.Hp, 3);

            Tick(sim);

            Assert.IsFalse(agent.Alive);
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Died));
            Assert.AreEqual(1, sim.PileAt(2, 4).Count, "its berries are left behind");
        }

        [Test]
        public void A_territorial_animal_goes_home_instead_of_chasing_beyond_its_leash()
        {
            var sim = Field();
            int leash = Kit.Data.ConfigInt("territorial_leash");
            var bear = sim.SpawnAnimal("bear", 4 + leash + 2, 4);
            bear.HomeX = 4;
            sim.AddAgent(bear.X + 2, 4, Kit.Neutral);
            int start = bear.X;

            Tick(sim, 4);

            Assert.Less(bear.X, start, "it turned back toward home although a villager was in reach");
        }

        [Test]
        public void Bare_hands_hit_for_the_bare_hand_damage()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            var bear = sim.SpawnAnimal("bear", 3, 4);

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(Def("bear").Hp - Kit.Data.ConfigFloat("bare_hand_damage"), bear.Hp, 1e-4f);
            Assert.IsTrue(agent.LastActionValid);
        }

        [Test]
        public void A_weapon_hits_for_its_own_damage_and_wears()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, "wooden_spear");
            var bear = sim.SpawnAnimal("bear", 3, 4);
            var spear = Kit.Data.EquipmentByItemInt(Kit.Item("wooden_spear"));
            int durability = agent.Equipped[(int)EquipSlot.Hand].Durability;

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(Def("bear").Hp - spear.AttackDamage, bear.Hp, 1e-4f);
            Assert.AreEqual(durability - 1, agent.Equipped[(int)EquipSlot.Hand].Durability);
        }

        [Test]
        public void A_spear_reaches_as_far_as_its_range_and_no_further()
        {
            int range = Kit.Data.EquipmentByItemInt(Kit.Item("wooden_spear")).AttackRange;
            Assert.Greater(range, 1);
            var near = Field();
            var hunter = Armed(near, 2, 4, "wooden_spear");
            near.SpawnAnimal("bear", 2 + range, 4);
            Assert.IsTrue(near.IsValid(hunter.Index, new AgentAction(near.Actions.Harvest)));

            var far = Field();
            var other = Armed(far, 2, 4, "wooden_spear");
            far.SpawnAnimal("bear", 2 + range + 1, 4);
            Assert.IsFalse(far.IsValid(other.Index, new AgentAction(far.Actions.Harvest)));
        }

        [Test]
        public void Empty_hands_only_reach_the_next_tile()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            sim.SpawnAnimal("bear", 4, 4);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Harvest)));
        }

        [Test]
        public void A_tree_in_the_way_is_chopped_instead_of_hitting_the_animal_behind_it()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, "wooden_spear");
            sim.World.PlaceNode(3, 4, Kit.Data.NodeById("pine_tree"));
            var bear = sim.SpawnAnimal("bear", 4, 4);

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(Def("bear").Hp, bear.Hp);
            Assert.Greater(sim.World.NodeProgress[sim.World.Index(3, 4)], 0f);
            Assert.IsTrue(agent.LastActionValid);
        }

        [Test]
        public void Attacking_costs_energy_like_other_work()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            sim.SpawnAnimal("bear", 3, 4);
            float before = agent.Get(Need.Energy);

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            float cost = Kit.Data.Actions[sim.Actions.Harvest].EnergyCost;
            Assert.AreEqual(before - cost - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Killing_an_animal_puts_its_loot_in_the_hunters_pack()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            var deer = sim.SpawnAnimal("deer", 3, 4);
            deer.Hp = 1;

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            var def = Def("deer");
            Assert.IsFalse(deer.Alive);
            Assert.IsNull(sim.AnimalAt(3, 4));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.AnimalKilled && e.Agent == agent.Index));
            Assert.AreEqual(def.LootRawMeat, agent.Count(Kit.Item("raw_meat")));
            Assert.AreEqual(def.LootAnimalHide, agent.Count(Kit.Item("animal_hide")));
            Assert.AreEqual(def.LootBone, agent.Count(Kit.Item("bone")));
        }

        [Test]
        public void Loot_that_does_not_fit_is_left_in_a_pile_where_the_animal_fell()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            for (int i = 0; agent.FreeSlots > 1; i++) sim.AddItem(agent, Kit.Item(Village.Filler[i]), 1);
            var deer = sim.SpawnAnimal("deer", 3, 4);
            deer.Hp = 1;

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(Def("deer").LootRawMeat, agent.Count(Kit.Item("raw_meat")), "the first kind of loot fitted");
            Assert.AreEqual(Kit.Data.NodeById("item_pile").Int, sim.World.NodeAt(3, 4));
            Assert.AreEqual(2, sim.PileAt(3, 4).Count, "hide and bone are on the ground");
        }

        [Test]
        public void A_dead_animal_no_longer_acts()
        {
            var sim = Field();
            var agent = Armed(sim, 2, 4, null);
            var wolf = sim.SpawnAnimal("wolf", 3, 4);
            wolf.Hp = 1;

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });
            Tick(sim, Def("wolf").AttackCooldownTicks + 1);

            Assert.AreEqual(100f, agent.Get(Need.Hp), 0.01f, "the villager struck first and was never bitten");
        }

        [Test]
        public void A_new_map_is_stocked_with_each_species_away_from_the_village()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 4 });
            var world = sim.World;
            int distance = Kit.Data.ConfigInt("animal_village_distance");

            foreach (var def in Kit.Data.Animals)
            {
                int count = sim.Animals.Count(a => a.Def == def);
                Assert.AreEqual(sim.AnimalTarget(def.Id), count, def.Id);
                Assert.Greater(count, 0, def.Id);
            }
            foreach (var animal in sim.Animals)
            {
                Assert.GreaterOrEqual(Math.Max(Math.Abs(animal.X - world.SpawnX), Math.Abs(animal.Y - world.SpawnY)), distance, animal.Def.Id);
                Assert.AreSame(animal, sim.AnimalAt(animal.X, animal.Y));
            }
        }

        [Test]
        public void Animal_numbers_follow_the_density_in_the_data()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 4 });
            int walkable = 0;
            for (int i = 0; i < sim.World.Terrain.Length; i++)
                if (Kit.Data.TerrainByInt(sim.World.Terrain[i]).Walkable != 0) walkable++;

            foreach (var def in Kit.Data.Animals)
                Assert.AreEqual(Math.Max(1, (int)Math.Round(def.SpawnPer1000Tiles * walkable / 1000.0)), sim.AnimalTarget(def.Id), def.Id);
        }

        [Test]
        public void A_species_that_lost_animals_gets_new_ones_over_time()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 4 });
            var rabbits = sim.Animals.Where(a => a.Def.Id == "rabbit").ToList();
            foreach (var rabbit in rabbits) sim.DespawnAnimal(rabbit);
            Assert.AreEqual(0, sim.Animals.Count(a => a.Alive && a.Def.Id == "rabbit"));

            for (int t = 0; t <= Kit.Data.ConfigInt("animal_respawn_interval_ticks"); t++)
            {
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                Tick(sim);
            }

            Assert.AreEqual(1, sim.Animals.Count(a => a.Alive && a.Def.Id == "rabbit"), "one came back after one interval");
        }

        [Test]
        public void A_plain_simulation_has_no_animals_until_asked()
        {
            var sim = Field();

            Assert.IsEmpty(sim.Animals);
        }
    }
}
