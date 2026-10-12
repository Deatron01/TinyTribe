namespace TinyTribe.Sim.Core
{
    /// <summary>Indexes match obs_index in the Sim Needs sheet.</summary>
    public enum Need { Hp = 0, Hunger = 1, Thirst = 2, Energy = 3, Temp = 4, Morale = 5 }

    public enum EquipSlot { Hand = 0, Body = 1, Feet = 2, Head = 3, Back = 4 }

    public struct ItemStack
    {
        public int Item;
        public int Qty;
        public int Durability;
        public bool IsEmpty => Qty <= 0;
    }

    public struct AgentStats
    {
        public int Str, Dex, Int, End;
        public float Influence, Empathy, Sociability, Aggression;
    }

    /// <summary>One agent's choice for a tick: an action id from Sim Actions plus an argument from Sim Action Args.</summary>
    public readonly struct AgentAction
    {
        public readonly int Id;
        public readonly int Arg;

        public AgentAction(int id, int arg = 0)
        {
            Id = id;
            Arg = arg;
        }
    }

    /// <summary>How to set up a new simulation. Zero, negative or null fields fall back to Sim Config.</summary>
    public sealed class SimulationOptions
    {
        public ulong Seed = 1;
        public int Agents = -1;
        public int MapWidth;
        public int MapHeight;
        public string StartSeason;

        /// <summary>Rain, storms and heatwaves by the chances of the season.</summary>
        public bool Weather = true;

        /// <summary>Whether the Council goes over the jobs again as the village changes (R41), or gives each villager a job once.</summary>
        public bool JobReviews = true;

        /// <summary>Whether villagers can fall sick. Off unless asked for: as the numbers stand, an infection has no cure a young village can reach.</summary>
        public bool Sickness;

        /// <summary>How many villagers a village may grow to; 0 for the workbook's max_agents.</summary>
        public int MaxAgents;

        /// <summary>
        /// How many villagers a village may have had in all, the dead counted too; 0 for no limit. No
        /// child is born beyond it. For a trainer, whose tables have a fixed number of rows per village.
        /// </summary>
        public int MaxVillagersEver;
    }

    public enum SimEventType
    {
        InvalidAction, Harvested, NodeDepleted, NodeRegrown, Consumed, ToolBroke, Died,
        NodeChanged, Crafted, BuildingStarted, BuildingCompleted, Deposited, Taken, CampfireOut, HomeClaimed, AgentHurt, AnimalKilled,

        /// <summary>Agent is the villager the vote is about (-1 if nobody); Item is the VoteKind.</summary>
        VoteOpened,

        /// <summary>Agent is the voter; Item is the option chosen.</summary>
        VoteCast,

        /// <summary>Agent is the villager the vote was about; Item is the VoteKind; Qty is the winning option, 0 when nothing was decided.</summary>
        VoteClosed,

        /// <summary>Agent got the role in Item.</summary>
        RoleAssigned,

        /// <summary>Item is the place of the new weather in the Sim Weather list; Qty is how many ticks it will last.</summary>
        WeatherChanged,

        /// <summary>Item is the building_int; Qty is the building's index.</summary>
        BuildingDestroyed,

        /// <summary>Item is the place of the technology in the Sim Tech list.</summary>
        TechResearched,

        /// <summary>Agent planted the crop whose place in the Sim Crops list is Item.</summary>
        CropPlanted,

        /// <summary>The terrain of the tile changed; Item is the new terrain_int.</summary>
        TerrainChanged,

        /// <summary>Agent fell sick; Item is the place of the sickness in the Sim Sicknesses list.</summary>
        SicknessStarted,

        /// <summary>Agent got over a sickness or was cured; Item is the place of the sickness in the Sim Sicknesses list.</summary>
        SicknessEnded,

        /// <summary>Agent showed the emote in Item.</summary>
        Emoted,

        /// <summary>A happy emote reached the villager in Item: both think better of each other.</summary>
        EmoteExchanged,

        /// <summary>Agent gave one of Item to the villager in Qty.</summary>
        Gave,

        /// <summary>Agent and the villager in Item married.</summary>
        Married,

        /// <summary>Agent was born to the villagers in Item and Qty.</summary>
        Born,

        /// <summary>Agent came of age: no longer a child.</summary>
        CameOfAge,
    }

    /// <summary>Something that happened during the last Step. Read by the view, the logger and (later) the rewards.</summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly int Agent;
        public readonly int Item;
        public readonly int Qty;
        public readonly int X;
        public readonly int Y;

        public SimEvent(SimEventType type, int agent, int item, int qty, int x, int y)
        {
            Type = type;
            Agent = agent;
            Item = item;
            Qty = qty;
            X = x;
            Y = y;
        }
    }
}
