using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Research at the Library (Sim Tech, rules R18 and R38) and farm plots (Sim Crops, rule R06).</summary>
    public sealed partial class Simulation
    {
        // Ids and keys from Sim Action Args, Sim Buildings, Sim Terrain, Sim Resource Nodes and Sim Equipment.
        const string ArgResearch = "research", ArgPlantCrop = "plant_crop";
        const string EffectResearchPoints = "rp_per_tick";
        const string EffectBasicResearchPoints = "rp_per_tick_basic";
        const string EffectCropSlots = "crop_slots";
        const string FarmlandTerrain = "farmland";
        const string CropGrowingNode = "crop_growing", CropMatureNode = "crop_mature";
        const string HoeTool = "hoe";

        sealed class Crop
        {
            public CropDef Def;
            public int DefIndex;
            public float Growth;

            /// <summary>1, or more when a Farmer planted it.</summary>
            public float Pace;
        }

        /// <summary>Planted tiles, growing or ripe, by tile index.</summary>
        readonly SortedDictionary<int, Crop> crops = new SortedDictionary<int, Crop>();
        readonly List<int> deadCrops = new List<int>();
        CropDef[] cropByArg;
        int[] cropIndexByArg;
        bool[][] cropSownInSeason;
        int researchArg = -1;
        int farmlandInt;
        ResourceNodeDef cropGrowing, cropMature;
        int scholarRole, farmerRole;
        float scholarResearchMult = 1f, farmerGrowthMult = 1f;
        TechDef researching;
        float researchPoints;
        long nextResearchVoteTick;

        /// <summary>The technology the Council chose and the Library is working on, or null.</summary>
        public TechDef ResearchingTech => researching;

        /// <summary>Research points gathered toward ResearchingTech.</summary>
        public float ResearchPoints => researchPoints;

        /// <summary>The crop planted on the tile, growing or ripe, or null.</summary>
        public CropDef CropAt(int x, int y) =>
            World.InBounds(x, y) && crops.TryGetValue(World.Index(x, y), out var crop) ? crop.Def : null;

        void InitFarming()
        {
            cropByArg = new CropDef[argCount];
            cropIndexByArg = new int[argCount];
            foreach (var arg in Data.ActionArgs)
            {
                if (arg.Kind == ArgResearch) researchArg = arg.Int;
                if (arg.Kind != ArgPlantCrop) continue;
                for (int i = 0; i < Data.Crops.Count; i++)
                {
                    if (Data.Crops[i].Id != arg.RefId) continue;
                    cropByArg[arg.Int] = Data.Crops[i];
                    cropIndexByArg[arg.Int] = i;
                }
            }
            cropSownInSeason = new bool[Data.Crops.Count][];
            for (int i = 0; i < Data.Crops.Count; i++)
            {
                var sown = (Data.Crops[i].PlantSeasons ?? "").Split(';');
                cropSownInSeason[i] = new bool[Data.Seasons.Count];
                foreach (var season in Data.Seasons) cropSownInSeason[i][season.Order] = Array.IndexOf(sown, season.Id) >= 0;
            }
            farmlandInt = Data.TerrainById(FarmlandTerrain).Int;
            cropGrowing = Data.NodeById(CropGrowingNode);
            cropMature = Data.NodeById(CropMatureNode);
            foreach (var role in Data.Roles)
            {
                if (role.Id == "scholar")
                {
                    scholarRole = role.Int;
                    scholarResearchMult = role.BonusValue;
                }
                else if (role.Id == "farmer")
                {
                    farmerRole = role.Int;
                    farmerGrowthMult = role.BonusValue;
                }
            }
            // What costs nothing is known from the start.
            foreach (var tech in Data.Tech)
                if (tech.RpCost <= 0f) researched.Add(tech.Id);
        }

        // ------------------------------------------------------------------ research

        /// <summary>Not known yet, its prerequisite is, and the village has a place that can teach it.</summary>
        bool CanResearch(TechDef tech) => !researched.Contains(tech.Id) && IsResearched(tech.Prerequisite) && SomePlaceTeaches(tech);

        bool AnythingToResearch()
        {
            foreach (var tech in Data.Tech)
                if (CanResearch(tech)) return true;
            return false;
        }

        /// <summary>R40: a Library teaches everything; an Elders' Circle only what does not need a library.</summary>
        static bool Teaches(Building place, TechDef tech) =>
            place != null && place.Complete
            && (place.Def.EffectKey == EffectResearchPoints || (place.Def.EffectKey == EffectBasicResearchPoints && tech.NeedsLibrary == 0));

        bool SomePlaceTeaches(TechDef tech)
        {
            foreach (var building in buildings)
                if (Teaches(building, tech)) return true;
            return false;
        }

        /// <summary>R38: craft_build with the research argument, facing a place that can teach the chosen technology, adds points to it.</summary>
        bool TryResearch(Agent agent, bool execute)
        {
            if (researching == null) return false;
            var place = BuildingFacing(agent);
            if (!Teaches(place, researching)) return false;
            if (!execute) return true;

            AddNeed(agent, Need.Energy, -craftEnergyCost * agent.EnduranceMult * weatherWorkEnergyMult);
            float byJob = agent.Role == scholarRole ? scholarResearchMult : Config.ResearchRpMultNonScholar;
            // The place of study sets the pace: a Library's is rp_per_tick_base, a circle's is slower.
            float studied = place.Def.EffectValue * agent.Stats.Int / (float)Config.StatMidpoint * byJob;
            researchPoints += studied;
            Pay(agent, researchPoint, studied);
            if (researchPoints + 1e-4f < researching.RpCost) return true;

            researched.Add(researching.Id);
            JobsMayHaveChanged();
            int index = 0;
            for (int i = 0; i < Data.Tech.Count; i++)
                if (Data.Tech[i] == researching) index = i;
            events.Add(new SimEvent(SimEventType.TechResearched, agent.Index, index, 0, agent.X, agent.Y));
            researching = null;
            researchPoints = 0f;
            return true;
        }

        /// <summary>R18: with a place of study standing and nothing being researched, the Council votes on what to research.</summary>
        void OpenResearchVote()
        {
            if (researching != null || Tick < nextResearchVoteTick || !AnythingToResearch()) return;
            openVote = new Vote { Kind = VoteKind.Research, OpenedTick = Tick, ClosesTick = Tick + Config.VoteDurationTicks };
            events.Add(new SimEvent(SimEventType.VoteOpened, -1, (int)VoteKind.Research, 0, World.SpawnX, World.SpawnY));
        }

        void ResearchVoteClosed(int winner)
        {
            if (winner == 0)
            {
                // Nobody could agree: ask again tomorrow.
                nextResearchVoteTick = Tick + Config.TicksPerDay;
                return;
            }
            researching = Data.Tech[winner];
            researchPoints = 0f;
        }

        // ------------------------------------------------------------------ farming

        /// <summary>A finished farm turns free ground along its sides into farmland: below it first, then above, left and right.</summary>
        void LayFarmland(Building farm, int plots)
        {
            for (int side = 0; side < 4 && plots > 0; side++)
            {
                int length = side < 2 ? farm.Width : farm.Height;
                for (int i = 0; i < length && plots > 0; i++)
                {
                    int x = side < 2 ? farm.X + i : side == 2 ? farm.X - 1 : farm.X + farm.Width;
                    int y = side == 0 ? farm.Y - 1 : side == 1 ? farm.Y + farm.Height : farm.Y + i;
                    if (!World.InBounds(x, y)) continue;
                    int tile = World.Index(x, y);
                    if (!terrainWalkable[World.Terrain[tile]] || World.Terrain[tile] == farmlandInt
                        || World.Node[tile] != 0 || World.BuildingIndex[tile] != -1) continue;
                    World.Terrain[tile] = (byte)farmlandInt;
                    events.Add(new SimEvent(SimEventType.TerrainChanged, -1, farmlandInt, 0, x, y));
                    plots--;
                }
            }
        }

        /// <summary>R06: one seed into the empty farmland tile in front, in a season the crop can be sown in.</summary>
        bool TryPlant(Agent agent, int arg, bool execute)
        {
            var crop = cropByArg[arg];
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (!World.InBounds(x, y)) return false;
            int tile = World.Index(x, y);
            if (World.Terrain[tile] != farmlandInt || World.Node[tile] != 0 || World.AgentIndex[tile] != -1
                || World.AnimalIndex[tile] != -1 || World.BuildingIndex[tile] != -1) return false;
            if (!cropSownInSeason[cropIndexByArg[arg]][Season.Order]) return false;
            int seed = Data.ItemById[crop.SeedItem].Int;
            if (agent.Count(seed) == 0) return false;
            if (!execute) return true;

            Slots.Remove(agent.Inventory, agent.InventoryCapacity, seed, 1);
            AddNeed(agent, Need.Energy, -craftEnergyCost * agent.EnduranceMult * weatherWorkEnergyMult);
            World.PlaceNode(x, y, cropGrowing);
            crops[tile] = new Crop { Def = crop, DefIndex = cropIndexByArg[arg], Pace = agent.Role == farmerRole ? farmerGrowthMult : 1f };
            events.Add(new SimEvent(SimEventType.CropPlanted, agent.Index, cropIndexByArg[arg], 1, x, y));
            events.Add(new SimEvent(SimEventType.NodeChanged, -1, cropGrowing.Int, 0, x, y));
            return true;
        }

        /// <summary>Crops grow at the pace of the season, ripen, and die in the field when nothing grows at all.</summary>
        void UpdateCrops()
        {
            if (crops.Count == 0) return;
            float pace = Season.CropGrowthMult;
            foreach (var planted in crops)
            {
                int tile = planted.Key;
                var crop = planted.Value;
                if (pace <= 0f)
                {
                    if (crop.Def.DiesInWinter != 0) deadCrops.Add(tile);
                    continue;
                }
                if (World.Node[tile] != cropGrowing.Int) continue;
                crop.Growth += pace * crop.Pace;
                if (crop.Growth + 1e-4f < crop.Def.GrowthTicks) continue;

                World.Node[tile] = (short)cropMature.Int;
                World.NodeCharges[tile] = (short)Math.Max(1, cropMature.Charges);
                World.NodeProgress[tile] = 0f;
                events.Add(new SimEvent(SimEventType.NodeChanged, -1, cropMature.Int, 0, tile % World.Width, tile / World.Width));
            }

            foreach (int tile in deadCrops)
            {
                crops.Remove(tile);
                World.RemoveNode(tile);
                events.Add(new SimEvent(SimEventType.NodeChanged, -1, 0, 0, tile % World.Width, tile / World.Width));
            }
            deadCrops.Clear();
        }

        /// <summary>The crop comes off the tile with its yield; its seed goes back in the pack, or on the freed ground if it does not fit.</summary>
        void ReapCrop(Agent agent, int tile)
        {
            if (!crops.TryGetValue(tile, out var crop)) return;
            crops.Remove(tile);
            if (crop.Def.SeedReturn <= 0) return;
            var seed = Data.ItemById[crop.Def.SeedItem];
            int fitted = AddItem(agent, seed, crop.Def.SeedReturn, 0);
            if (fitted > 0) events.Add(new SimEvent(SimEventType.Harvested, agent.Index, seed.Int, fitted, tile % World.Width, tile / World.Width));
            if (fitted < crop.Def.SeedReturn) AddToPile(tile, new ItemStack { Item = seed.Int, Qty = crop.Def.SeedReturn - fitted });
        }
    }
}
