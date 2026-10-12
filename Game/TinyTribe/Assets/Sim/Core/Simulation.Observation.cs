using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    public struct ObservationField
    {
        public int Start;
        public int Length;
    }

    /// <summary>Sizes of an observation and where each field of the vector lies (the Sim Observation sheet).</summary>
    public sealed class ObservationLayout
    {
        public int GridSize, GridChannels, GridLength, VectorLength, ActionCount, ArgCount;

        /// <summary>How many different values each channel of the grid can hold (0 included): what a one-hot encoding needs.</summary>
        public int[] ChannelSizes;

        /// <summary>
        /// An item in the vector is its number divided by ItemScale, a job its number divided by RoleScale:
        /// what a reader multiplies by to get the item or the job back.
        /// </summary>
        public int ItemScale, RoleScale;

        /// <summary>By the field's name in the sheet.</summary>
        public readonly Dictionary<string, ObservationField> Fields = new Dictionary<string, ObservationField>();

        internal int Add(string name, int length)
        {
            Fields[name] = new ObservationField { Start = VectorLength, Length = length };
            VectorLength += length;
            return VectorLength - length;
        }
    }

    /// <summary>
    /// What a villager sees (Sim Observation): the grid around it, a vector about itself and its
    /// village, and the masks of what it can do. Nothing here decides anything; it is the villager's
    /// eyes, and all a network has to go on.
    /// </summary>
    public sealed partial class Simulation
    {
        // Channels of the grid.
        const int TerrainChannel = 0, EntityChannel = 1, ResourceChannel = 2, StructureChannel = 3, EmoteChannel = 4;
        const int OtherAgent = 1, HouseMate = 2;

        /// <summary>The emote_int rows of the sheet. The ones an action shows are in Simulation.Family.cs; the automatic ones are not built.</summary>
        const int EmoteCount = 12;

        // From the encodings in the sheet.
        const int PackSlotsSeen = 10, SkillCount = 7, TraitCount = 12, IdeologyCount = 4;
        const float StockScale = 100f, WorksScale = 4f;
        const string DoctorRole = "doctor";
        static readonly string[] StoneItems = { "cobblestone", "stone_bricks", "marble" };
        static readonly string[] MetalItems = { "copper_ore", "iron_ore", "gold_ore", "copper_ingot", "iron_ingot", "gold_ingot" };

        // The compass: what a villager knows of its surroundings beyond the 11 x 11 tiles it sees.
        public const int CompassWater = 0, CompassFood = 1, CompassWood = 2, CompassStone = 3, CompassCotton = 4, CompassOak = 5;
        const int CompassStorage = 6, CompassFire = 7, CompassSite = 8;
        const int CompassNodeKinds = 6, CompassKinds = 9;
        const string WaterNodeId = "water_source", StoneItemId = "cobblestone", WoodItemId = "pine_wood", CottonItemId = "cotton", OakItemId = "oak_wood";
        static readonly int[] SeekStepX = { 1, -1, 0, 0 }, SeekStepY = { 0, 0, 1, -1 };

        ObservationLayout layout;
        int oCompass, compassRadius;
        sbyte[] nodeCompassKind = new sbyte[0];
        readonly int[] compassDx = new int[CompassKinds], compassDy = new int[CompassKinds];
        readonly bool[] compassFound = new bool[CompassKinds];
        // Scratch space of the search for the nearest things, reused between searches.
        readonly int[] seekTile = new int[CompassNodeKinds];
        int[] seekQueue = new int[0], seekStamp = new int[0], seekSteps = new int[0];
        int seekStampNow;
        int oNeeds, oCore, oSocial, oPack, oTool, oClothing, oTime, oSeason, oWeather, oRole, oAge, oSickness, oStatus, oHome, oVillage;
        int oFacedProgress, oFacedAgent, oStock, oState, oRoleCounts, oVote, oVoteRole, oVoteReview, oWorks, oTechKnown, oTechStudied;
        float itemScale, roleScale;
        int doctorRole;
        bool[] itemIsWood, itemIsStone, itemIsMetal;
        int[] obsStored = new int[0], obsRoleCounts = new int[0], obsWorks = new int[0];
        bool[] maskRow = new bool[0];

        /// <summary>Sizes and offsets of what Observe writes.</summary>
        public ObservationLayout Observation => layout;

        public bool TakesArgument(int actionId) => (uint)actionId < (uint)actionCount && actionTakesArg[actionId];

        void InitObservation()
        {
            layout = new ObservationLayout
            {
                GridSize = Data.ConfigInt("fov_size"), GridChannels = Data.ConfigInt("fov_channels"), ActionCount = actionCount, ArgCount = argCount,
            };
            layout.GridLength = layout.GridSize * layout.GridSize * layout.GridChannels;
            int terrains = 0, entities = HouseMate, nodes = 0, structures = 0;
            foreach (var terrain in Data.Terrain) terrains = Math.Max(terrains, terrain.Int);
            foreach (var animal in Data.Animals) entities = Math.Max(entities, animal.Int);
            foreach (var node in Data.ResourceNodes) nodes = Math.Max(nodes, node.Int);
            foreach (var building in Data.Buildings) structures = Math.Max(structures, building.Int);
            layout.ChannelSizes = new int[layout.GridChannels];
            for (int channel = 0; channel < layout.GridChannels; channel++)
                layout.ChannelSizes[channel] = 1 + (channel == TerrainChannel ? terrains : channel == EntityChannel ? entities
                    : channel == ResourceChannel ? nodes : channel == StructureChannel ? structures : EmoteCount);
            int buildingInts = 0;
            foreach (var building in Data.Buildings) buildingInts = Math.Max(buildingInts, building.Int);

            oNeeds = layout.Add("needs", Data.Needs.Count);
            oCore = layout.Add("core stats", 4);
            oSocial = layout.Add("social stats", 4);
            oPack = layout.Add("inventory", PackSlotsSeen * 2);
            oTool = layout.Add("equipped tool", 2);
            oClothing = layout.Add("clothing", 4);
            oTime = layout.Add("time of day", 2);
            oSeason = layout.Add("season", Data.Seasons.Count);
            oWeather = layout.Add("weather", Data.Weather.Count);
            oRole = layout.Add("role", 1);
            oAge = layout.Add("age", 1);
            oSickness = layout.Add("sickness flags", Data.Sicknesses.Count);
            layout.Add("skill levels", SkillCount);
            layout.Add("traits", TraitCount);
            layout.Add("ideology", IdeologyCount);
            oStatus = layout.Add("status flags", 4);
            oHome = layout.Add("home direction", 2);
            oVillage = layout.Add("village direction", 2);
            oFacedProgress = layout.Add("faced progress", 1);
            oFacedAgent = layout.Add("faced agent", 5);
            oStock = layout.Add("village stock", 4);
            oState = layout.Add("village state", 6);
            oRoleCounts = layout.Add("role counts", roleCount - 1);
            oVote = layout.Add("vote context", 3 + 8);
            layout.Add("wealth", 2);
            oVoteRole = layout.Add("vote subject role", 1);
            oVoteReview = layout.Add("vote is review", 1);
            oWorks = layout.Add("village works", buildingInts);
            oTechKnown = layout.Add("technologies known", Data.Tech.Count);
            oTechStudied = layout.Add("technology studied", 1);
            oCompass = layout.Add("compass", CompassKinds * 3);
            compassRadius = Data.ConfigInt("obs_compass_radius");
            int nodeInts = 0;
            foreach (var node in Data.ResourceNodes) nodeInts = Math.Max(nodeInts, node.Int);
            nodeCompassKind = new sbyte[nodeInts + 1];
            foreach (var node in Data.ResourceNodes)
            {
                Data.ItemById.TryGetValue(node.YieldItem ?? "", out var yields);
                // Food is what can be eaten as it is picked and does no harm.
                nodeCompassKind[node.Int] = (sbyte)(node.Id == WaterNodeId ? CompassWater
                    : yields == null ? -1
                    : yields.Consumable != 0 && yields.Hunger > 0f && string.IsNullOrEmpty(yields.SicknessOnEat) ? CompassFood
                    : yields.Id == WoodItemId ? CompassWood
                    : yields.Id == StoneItemId ? CompassStone
                    : yields.Id == CottonItemId ? CompassCotton
                    : yields.Id == OakItemId ? CompassOak : -1);
            }
            int tiles = World.Width * World.Height;
            seekQueue = new int[tiles];
            seekStamp = new int[tiles];
            seekSteps = new int[tiles];

            int items = 0;
            foreach (var item in Data.Items) items = Math.Max(items, item.Int);
            itemScale = Math.Max(1, items);
            roleScale = Math.Max(1, roleCount - 1);
            layout.ItemScale = (int)itemScale;
            layout.RoleScale = (int)roleScale;
            itemIsWood = new bool[items + 1];
            itemIsStone = new bool[items + 1];
            itemIsMetal = new bool[items + 1];
            foreach (var item in Data.Items)
            {
                itemIsWood[item.Int] = item.StorageGroup == "wood";
                itemIsStone[item.Int] = Array.IndexOf(StoneItems, item.Id) >= 0;
                itemIsMetal[item.Int] = Array.IndexOf(MetalItems, item.Id) >= 0;
            }
            obsStored = new int[items + 1];
            obsRoleCounts = new int[roleCount];
            obsWorks = new int[buildingInts + 1];
            maskRow = new bool[argCount];
            foreach (var role in Data.Roles)
                if (role.Id == DoctorRole) doctorRole = role.Int;
        }

        /// <summary>
        /// Fills the villager's view. grid is GridSize x GridSize x GridChannels, row by row with the
        /// channels of a tile together; the top row is the north, the villager is in the middle.
        /// vector is VectorLength floats as laid out in Observation.Fields.
        /// </summary>
        public void Observe(int agentIndex, byte[] grid, float[] vector)
        {
            var agent = agents[agentIndex];
            ObserveGrid(agent, grid);
            Array.Clear(vector, 0, layout.VectorLength);
            ObserveSelf(agent, vector);
            ObserveVillage(agent, vector);
            ObserveCompass(agent, vector);
        }

        /// <summary>
        /// The nearest thing of a kind (CompassWater to CompassOak) that the villager could use as it is
        /// now, the nearest by the way it would walk: what its compass shows. Water counts whether or not
        /// the villager has room to carry any, since it can drink there; everything else must be something
        /// it could harvest with what it holds and carries. False when there is none within reach.
        /// </summary>
        public bool CompassTarget(int agentIndex, int kind, out int tile)
        {
            tile = -1;
            if ((uint)kind >= CompassNodeKinds) return false;
            if ((SeekNodes(agents[agentIndex], 1 << kind) & (1 << kind)) == 0) return false;
            tile = seekTile[kind];
            return true;
        }

        /// <summary>
        /// Walks outward from the villager over open ground, a step at a time, as far as it can go and
        /// still touch something within obs_compass_radius steps, and notes the first node of each wanted kind it comes to stand beside
        /// (seekTile). Nodes and buildings are walked around; other villagers and animals move, so they
        /// are not. Always in the same order, so a tie is always broken the same way. Returns the kinds found, as bits.
        /// </summary>
        int SeekNodes(Agent agent, int wanted)
        {
            int width = World.Width, found = 0, head = 0, tail = 0;
            int start = World.Index(agent.X, agent.Y);
            seekStampNow++;
            seekQueue[tail++] = start;
            seekStamp[start] = seekStampNow;
            seekSteps[start] = 0;
            while (head < tail && found != wanted)
            {
                int tile = seekQueue[head++];
                int x = tile % width, y = tile / width, steps = seekSteps[tile];
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + SeekStepX[d], ny = y + SeekStepY[d];
                    if (!World.InBounds(nx, ny)) continue;
                    int next = World.Index(nx, ny);
                    if (World.HasActiveNode(next))
                    {
                        int kind = nodeCompassKind[World.Node[next]];
                        int bit = kind < 0 ? 0 : 1 << kind;
                        if ((wanted & bit) != 0 && (found & bit) == 0 && (kind == CompassWater || CouldHarvest(agent, nx, ny)))
                        {
                            found |= bit;
                            seekTile[kind] = next;
                        }
                        continue;
                    }
                    if (seekStamp[next] == seekStampNow || steps + 1 >= compassRadius) continue;
                    if (!terrainWalkable[World.Terrain[next]] || World.BuildingIndex[next] != -1) continue;
                    seekStamp[next] = seekStampNow;
                    seekSteps[next] = steps + 1;
                    seekQueue[tail++] = next;
                }
            }
            return found;
        }

        /// <summary>
        /// The way to the nearest water, food, wood, stone, cotton and oak the villager could use (see
        /// CompassTarget), and to the nearest storage, fire and building site wherever they are.
        /// </summary>
        void ObserveCompass(Agent agent, float[] v)
        {
            Array.Clear(compassFound, 0, CompassKinds);
            int found = SeekNodes(agent, (1 << CompassNodeKinds) - 1);
            for (int kind = 0; kind < CompassNodeKinds; kind++)
            {
                if ((found & (1 << kind)) == 0) continue;
                compassFound[kind] = true;
                compassDx[kind] = seekTile[kind] % World.Width - agent.X;
                compassDy[kind] = seekTile[kind] / World.Width - agent.Y;
            }

            int nearestStorage = int.MaxValue, nearestFire = int.MaxValue, nearestSite = int.MaxValue;
            foreach (var building in buildings)
            {
                if (building.Destroyed) continue;
                // To the nearest tile of the building, not to its corner.
                int dx = agent.X < building.X ? building.X - agent.X : agent.X >= building.X + building.Width ? building.X + building.Width - 1 - agent.X : 0;
                int dy = agent.Y < building.Y ? building.Y - agent.Y : agent.Y >= building.Y + building.Height ? building.Y + building.Height - 1 - agent.Y : 0;
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (!building.Complete) CompassNearer(CompassSite, dx, dy, distance, ref nearestSite);
                else if (IsCampfire(building.Def)) CompassNearer(CompassFire, dx, dy, distance, ref nearestFire);
                else if (building.Storage != null && !IsHouse(building.Def)) CompassNearer(CompassStorage, dx, dy, distance, ref nearestStorage);
            }

            for (int kind = 0; kind < CompassKinds; kind++)
            {
                if (!compassFound[kind]) continue;
                int dx = compassDx[kind], dy = compassDy[kind];
                float distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (distance > 0f)
                {
                    v[oCompass + kind * 3] = dx / distance;
                    v[oCompass + kind * 3 + 1] = dy / distance;
                }
                v[oCompass + kind * 3 + 2] = Math.Max(0f, 1f - distance / compassRadius);
            }
        }

        void CompassNearer(int kind, int dx, int dy, int distance, ref int nearest)
        {
            if (distance >= nearest) return;
            nearest = distance;
            compassFound[kind] = true;
            compassDx[kind] = dx;
            compassDy[kind] = dy;
        }

        void ObserveGrid(Agent agent, byte[] grid)
        {
            int size = layout.GridSize, channels = layout.GridChannels, half = size / 2;
            Array.Clear(grid, 0, layout.GridLength);
            for (int row = 0; row < size; row++)
            {
                int y = agent.Y + half - row;
                for (int column = 0; column < size; column++)
                {
                    int x = agent.X - half + column;
                    if (!World.InBounds(x, y)) continue;
                    int tile = World.Index(x, y), at = (row * size + column) * channels;
                    grid[at + TerrainChannel] = World.Terrain[tile];

                    int other = World.AgentIndex[tile];
                    if (other >= 0) grid[at + EmoteChannel] = (byte)EmoteOf(agents[other]);
                    if (other >= 0 && other != agent.Index)
                        grid[at + EntityChannel] = (byte)(agent.Home >= 0 && agents[other].Home == agent.Home ? HouseMate : OtherAgent);
                    else if (World.AnimalIndex[tile] >= 0) grid[at + EntityChannel] = (byte)animals[World.AnimalIndex[tile]].Def.Int;

                    if (World.HasActiveNode(tile)) grid[at + ResourceChannel] = (byte)World.Node[tile];
                    int building = World.BuildingIndex[tile];
                    if (building >= 0) grid[at + StructureChannel] = (byte)buildings[building].Def.Int;
                }
            }
        }

        void ObserveSelf(Agent agent, float[] v)
        {
            float needMax = Config.NeedMax, statMax = Config.CoreStatMax;
            for (int need = 0; need < agent.Needs.Length; need++) v[oNeeds + need] = agent.Needs[need] / needMax;
            v[oCore] = agent.Stats.Str / statMax;
            v[oCore + 1] = agent.Stats.Dex / statMax;
            v[oCore + 2] = agent.Stats.Int / statMax;
            v[oCore + 3] = agent.Stats.End / statMax;
            v[oSocial] = agent.Stats.Influence;
            v[oSocial + 1] = agent.Stats.Empathy;
            v[oSocial + 2] = agent.Stats.Sociability;
            v[oSocial + 3] = agent.Stats.Aggression;

            for (int slot = 0; slot < agent.InventoryCapacity && slot < PackSlotsSeen; slot++)
            {
                var stack = agent.Inventory[slot];
                if (stack.Qty <= 0) continue;
                var item = Data.ItemByInt(stack.Item);
                v[oPack + slot * 2] = stack.Item / itemScale;
                v[oPack + slot * 2 + 1] = stack.Qty / (float)Math.Max(1, item != null ? item.MaxStack : 1);
            }
            var hand = agent.Equipped[(int)EquipSlot.Hand];
            if (hand.Qty > 0)
            {
                var tool = Data.EquipmentByItemInt(hand.Item);
                v[oTool] = hand.Item / itemScale;
                v[oTool + 1] = tool != null && tool.Durability > 0 ? Math.Min(1f, hand.Durability / (float)tool.Durability) : 1f;
            }
            v[oClothing] = agent.Equipped[(int)EquipSlot.Body].Qty > 0 ? 1f : 0f;
            v[oClothing + 1] = agent.Equipped[(int)EquipSlot.Feet].Qty > 0 ? 1f : 0f;
            v[oClothing + 2] = agent.Equipped[(int)EquipSlot.Head].Qty > 0 ? 1f : 0f;
            v[oClothing + 3] = agent.Equipped[(int)EquipSlot.Back].Qty > 0 ? 1f : 0f;

            double angle = 2.0 * Math.PI * (ClockTick % Config.TicksPerDay) / Config.TicksPerDay;
            v[oTime] = (float)Math.Sin(angle);
            v[oTime + 1] = (float)Math.Cos(angle);
            v[oSeason + Season.Order] = 1f;
            for (int i = 0; i < Data.Weather.Count; i++)
                if (Data.Weather[i] == weather) v[oWeather + i] = 1f;
            v[oRole] = agent.Role / roleScale;
            v[oAge] = Math.Min(1f, AgeOf(agent) / (float)oldAgeFrom);
            v[oStatus] = agent.Spouse >= 0 ? 1f : 0f;
            for (int i = 0; i < agent.SickTicks.Length && i < Data.Sicknesses.Count; i++)
                if (agent.SickTicks[i] != 0) v[oSickness + Data.Sicknesses[i].ObsFlagIndex] = 1f;

            // Jailed and convicted wait for their systems.
            if (agent.Home >= 0)
            {
                var home = buildings[agent.Home];
                v[oStatus + 3] = 1f;
                v[oHome] = Clamp((home.X - agent.X) / (float)World.Width);
                v[oHome + 1] = Clamp((home.Y - agent.Y) / (float)World.Height);
            }
            v[oVillage] = Clamp((World.SpawnX - agent.X) / (float)World.Width);
            v[oVillage + 1] = Clamp((World.SpawnY - agent.Y) / (float)World.Height);

            int faceX = agent.X + agent.FaceDx, faceY = agent.Y + agent.FaceDy;
            if (!World.InBounds(faceX, faceY)) return;
            int faced = World.Index(faceX, faceY);
            var site = BuildingAt(faceX, faceY);
            if (site != null && !site.Complete) v[oFacedProgress] = Math.Min(1f, site.Progress / Math.Max(1, site.Def.BuildTicks));
            else if (agent.CraftArg != 0 && recipeByArg[agent.CraftArg] != null)
                v[oFacedProgress] = Math.Min(1f, agent.CraftProgress / Math.Max(1, recipeByArg[agent.CraftArg].CraftTicks));
            else if (World.HasActiveNode(faced))
            {
                var node = Data.NodeByInt(World.Node[faced]);
                if (node != null && node.HarvestTicks > 0) v[oFacedProgress] = Math.Min(1f, World.NodeProgress[faced] / node.HarvestTicks);
            }

            int someone = World.AgentIndex[faced];
            if (someone < 0 || someone == agent.Index) return;
            var other = agents[someone];
            float health = other.Needs[(int)Need.Hp] / needMax;
            // Only a doctor can tell exactly; everyone else sees well, hurt or nearly gone.
            v[oFacedAgent] = agent.Role == doctorRole && doctorRole != 0 ? health : (float)Math.Round(health * 2f, MidpointRounding.AwayFromZero) / 2f;
            v[oFacedAgent + 1] = RelationshipOf(agent.Index, other.Index) / relationMax;
            v[oFacedAgent + 2] = other.Index == agent.Spouse ? 1f : 0f;
            v[oFacedAgent + 3] = other.Role / roleScale;
        }

        void ObserveVillage(Agent agent, float[] v)
        {
            VillageSummary.CountStoredItems(this, obsStored);
            int wood = 0, stone = 0, metal = 0, foodSlots = 0, foodSlotsUsed = 0;
            for (int item = 0; item < obsStored.Length; item++)
            {
                if (itemIsWood[item]) wood += obsStored[item];
                if (itemIsStone[item]) stone += obsStored[item];
                if (itemIsMetal[item]) metal += obsStored[item];
            }
            Array.Clear(obsWorks, 0, obsWorks.Length);
            foreach (var building in buildings)
            {
                if (!building.Complete) continue;
                if ((uint)building.Def.Int < (uint)obsWorks.Length) obsWorks[building.Def.Int]++;
                if (building.Storage == null || building.Def.StorageFilter != FoodStorage) continue;
                foodSlots += building.Storage.Length;
                foreach (var stack in building.Storage)
                    if (stack.Qty > 0) foodSlotsUsed++;
            }
            v[oStock] = foodSlots > 0 ? foodSlotsUsed / (float)foodSlots : 0f;
            v[oStock + 1] = Math.Min(1f, wood / StockScale);
            v[oStock + 2] = Math.Min(1f, stone / StockScale);
            v[oStock + 3] = Math.Min(1f, metal / StockScale);

            int alive = 0, starving = 0;
            Array.Clear(obsRoleCounts, 0, obsRoleCounts.Length);
            foreach (var other in agents)
            {
                if (!other.Alive) continue;
                alive++;
                if (other.Needs[(int)Need.Hunger] <= Config.NeedMin) starving++;
                if ((uint)other.Role < (uint)obsRoleCounts.Length) obsRoleCounts[other.Role]++;
            }
            v[oState] = alive / (float)Math.Max(1, Config.MaxAgents);
            // The Ages are not built: every village is in the Stone Age.
            v[oState + 1] = 1f;
            v[oState + 4] = researching != null && researching.RpCost > 0f ? Math.Min(1f, researchPoints / researching.RpCost) : 0f;
            v[oState + 5] = alive > 0 ? starving / (float)alive : 0f;
            for (int role = 1; role < obsRoleCounts.Length; role++)
                v[oRoleCounts + role - 1] = alive > 0 ? obsRoleCounts[role] / (float)alive : 0f;

            if (openVote != null)
            {
                if (openVote.Kind == VoteKind.Role) v[oVote] = 1f;
                else if (openVote.Kind == VoteKind.Research) v[oVote + 1] = 1f;
                else if (openVote.Kind == VoteKind.Conviction) v[oVote + 2] = 1f;
                if (openVote.Subject >= 0)
                {
                    var subject = agents[openVote.Subject];
                    float statMax = Config.CoreStatMax;
                    v[oVote + 3] = subject.Stats.Str / statMax;
                    v[oVote + 4] = subject.Stats.Dex / statMax;
                    v[oVote + 5] = subject.Stats.Int / statMax;
                    v[oVote + 6] = subject.Stats.End / statMax;
                    v[oVote + 7] = subject.Stats.Influence;
                    v[oVote + 8] = subject.Stats.Empathy;
                    v[oVote + 9] = subject.Stats.Sociability;
                    v[oVote + 10] = subject.Stats.Aggression;
                    if (openVote.Kind == VoteKind.Role) v[oVoteRole] = subject.Role / roleScale;
                }
                v[oVoteReview] = openVote.Review ? 1f : 0f;
            }

            for (int building = 1; building < obsWorks.Length; building++)
                v[oWorks + building - 1] = Math.Min(1f, obsWorks[building] / WorksScale);
            for (int i = 0; i < Data.Tech.Count; i++)
            {
                if (researched.Contains(Data.Tech[i].Id)) v[oTechKnown + i] = 1f;
                if (Data.Tech[i] == researching) v[oTechStudied] = i / (float)Math.Max(1, Data.Tech.Count - 1);
            }
        }

        static float Clamp(float value) => value < -1f ? -1f : value > 1f ? 1f : value;

        /// <summary>
        /// What the villager can do right now. actions[a] says whether action a is possible at all;
        /// args[a * ArgCount + i] is 1 when it is possible with argument i. An action that takes no
        /// argument is offered with argument 0. A policy picks an action, then an argument from that
        /// action's row, and can never ask for something the engine would refuse.
        /// </summary>
        public void GetMasks(int agentIndex, bool[] actions, byte[] args)
        {
            Array.Clear(args, 0, actionCount * argCount);
            for (int action = 0; action < actionCount; action++)
            {
                bool any = false;
                if (!actionTakesArg[action])
                {
                    any = IsValid(agentIndex, new AgentAction(action));
                    if (any) args[action * argCount] = 1;
                }
                else
                    for (int arg = 0; arg < argCount; arg++)
                    {
                        if (!IsValid(agentIndex, new AgentAction(action, arg))) continue;
                        args[action * argCount + arg] = 1;
                        any = true;
                    }
                actions[action] = any;
            }
        }
    }
}
