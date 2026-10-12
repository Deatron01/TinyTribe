using System;
using System.Globalization;
using System.Text;
using TinyTribe.Sim.Core;

namespace TinyTribe.Brains
{
    /// <summary>
    /// The state of a simulation written out line by line, every number exactly (floats with their
    /// bits). Two runs that should be the same and are not can be compared with any text diff; this is
    /// how a difference between machines or runtimes is tracked down to the value it starts in.
    /// </summary>
    public static class StateText
    {
        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        static string F(float value) => value.ToString("R", Plain) + "/" + BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8");

        public static string Describe(Simulation sim)
        {
            var text = new StringBuilder();
            text.Append("tick ").Append(sim.Tick).Append(" weather ").Append(sim.Weather.Id).Append(" ends ").Append(sim.WeatherEndsTick)
                .Append(" research ").Append(sim.ResearchingTech != null ? sim.ResearchingTech.Id : "-").Append(' ').Append(F(sim.ResearchPoints)).Append('\n');
            foreach (var agent in sim.Agents)
            {
                text.Append("agent ").Append(agent.Index).Append(agent.Alive ? " alive" : " dead").Append(" at ").Append(agent.X).Append(',').Append(agent.Y)
                    .Append(" facing ").Append(agent.FaceDx).Append(',').Append(agent.FaceDy).Append(" role ").Append(agent.Role)
                    .Append(" busy ").Append(agent.BusyTicks).Append(" craft ").Append(agent.CraftArg).Append(' ').Append(F(agent.CraftProgress))
                    .Append(" sleep ").Append(agent.Sleeping ? 1 : 0).Append(agent.ForcedSleep ? 1 : 0).Append(" home ").Append(agent.Home).Append('\n');
                text.Append("  needs");
                foreach (float need in agent.Needs) text.Append(' ').Append(F(need));
                text.Append("\n  pack");
                for (int i = 0; i < agent.InventoryCapacity; i++)
                    text.Append(' ').Append(agent.Inventory[i].Item).Append('x').Append(agent.Inventory[i].Qty).Append('d').Append(agent.Inventory[i].Durability);
                text.Append("\n  worn");
                foreach (var stack in agent.Equipped) text.Append(' ').Append(stack.Item).Append('x').Append(stack.Qty).Append('d').Append(stack.Durability);
                text.Append("\n  sick");
                foreach (int ticks in agent.SickTicks) text.Append(' ').Append(ticks);
                text.Append('\n');
            }
            foreach (var animal in sim.Animals)
                text.Append("animal ").Append(animal.Index).Append(' ').Append(animal.Def.Id).Append(animal.Alive ? " alive" : " dead").Append(" at ")
                    .Append(animal.X).Append(',').Append(animal.Y).Append(" hp ").Append(F(animal.Hp)).Append('\n');
            foreach (var building in sim.Buildings)
            {
                text.Append("building ").Append(building.Index).Append(' ').Append(building.Def.Id).Append(" at ").Append(building.X).Append(',').Append(building.Y)
                    .Append(building.Complete ? " done" : " site").Append(" progress ").Append(F(building.Progress)).Append(" fuel ").Append(building.FuelTicks);
                if (building.Storage != null)
                    foreach (var stack in building.Storage)
                        if (stack.Qty > 0) text.Append(' ').Append(stack.Item).Append('x').Append(stack.Qty);
                text.Append('\n');
            }
            var world = sim.World;
            for (int tile = 0; tile < world.Node.Length; tile++)
                if (world.Node[tile] != 0 && (world.NodeProgress[tile] != 0f || world.NodeCharges[tile] == 0))
                    text.Append("node ").Append(tile % world.Width).Append(',').Append(tile / world.Width).Append(' ').Append(world.Node[tile])
                        .Append(" charges ").Append(world.NodeCharges[tile]).Append(" progress ").Append(F(world.NodeProgress[tile])).Append('\n');
            return text.ToString();
        }
    }
}
