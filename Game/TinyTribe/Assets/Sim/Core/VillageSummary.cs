using System;

namespace TinyTribe.Sim.Core
{
    /// <summary>Totals for the resources window and (later) the village block of the observation.</summary>
    public static class VillageSummary
    {
        /// <summary>Everything living villagers carry or wear, by item_int. The array is cleared first.</summary>
        public static void CountHeldItems(Simulation sim, int[] totalsByItemInt)
        {
            Array.Clear(totalsByItemInt, 0, totalsByItemInt.Length);
            foreach (var agent in sim.Agents)
            {
                if (!agent.Alive) continue;
                for (int i = 0; i < agent.InventoryCapacity; i++)
                    Add(totalsByItemInt, agent.Inventory[i]);
                foreach (var stack in agent.Equipped)
                    Add(totalsByItemInt, stack);
            }
        }

        static void Add(int[] totals, ItemStack stack)
        {
            if (stack.Qty > 0 && (uint)stack.Item < (uint)totals.Length) totals[stack.Item] += stack.Qty;
        }

        /// <summary>Harvestable nodes on the map, by node_int. The array is cleared first.</summary>
        public static void CountActiveNodes(World world, int[] totalsByNodeInt)
        {
            Array.Clear(totalsByNodeInt, 0, totalsByNodeInt.Length);
            for (int tile = 0; tile < world.Node.Length; tile++)
            {
                int node = world.Node[tile];
                if (node != 0 && world.NodeCharges[tile] != 0 && node < totalsByNodeInt.Length) totalsByNodeInt[node]++;
            }
        }

        /// <summary>Everything in the storage of finished buildings, by item_int. The array is cleared first.</summary>
        public static void CountStoredItems(Simulation sim, int[] totalsByItemInt)
        {
            Array.Clear(totalsByItemInt, 0, totalsByItemInt.Length);
            foreach (var building in sim.Buildings)
            {
                if (!building.Complete || building.Storage == null) continue;
                foreach (var stack in building.Storage) Add(totalsByItemInt, stack);
            }
        }

        /// <summary>Finished buildings and building sites, by building_int. Both arrays are cleared first.</summary>
        public static void CountBuildings(Simulation sim, int[] finishedByBuildingInt, int[] sitesByBuildingInt)
        {
            Array.Clear(finishedByBuildingInt, 0, finishedByBuildingInt.Length);
            Array.Clear(sitesByBuildingInt, 0, sitesByBuildingInt.Length);
            foreach (var building in sim.Buildings)
            {
                if (building.Destroyed) continue;
                var totals = building.Complete ? finishedByBuildingInt : sitesByBuildingInt;
                if ((uint)building.Def.Int < (uint)totals.Length) totals[building.Def.Int]++;
            }
        }

        /// <summary>Living animals, by entity_int. The array is cleared first.</summary>
        public static void CountAnimals(Simulation sim, int[] aliveByEntityInt)
        {
            Array.Clear(aliveByEntityInt, 0, aliveByEntityInt.Length);
            foreach (var animal in sim.Animals)
                if (animal.Alive && (uint)animal.Def.Int < (uint)aliveByEntityInt.Length) aliveByEntityInt[animal.Def.Int]++;
        }

        public static int AliveCount(Simulation sim)
        {
            int alive = 0;
            foreach (var agent in sim.Agents)
                if (agent.Alive) alive++;
            return alive;
        }
    }
}
